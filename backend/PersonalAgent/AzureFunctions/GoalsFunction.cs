using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalAgent.Agents;
using PersonalAgent.Common;
using PersonalAgent.Data;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Backs the Objetivos page: lets the user describe their current stats + goals in a form,
/// generates a structured, research-grounded plan via GoalsAgent, persists it (GoalPlan row
/// + a WeightLog snapshot + auto-created Goal rows from the plan's targets), and lets the
/// page reload the last generated plan.
/// </summary>
public sealed class GoalsFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly GoalsAgent _goalsAgent;
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly ILogger<GoalsFunction> _logger;

    public GoalsFunction(
        GoalsAgent goalsAgent,
        ILogger<GoalsFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _goalsAgent = goalsAgent;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
    }

    public sealed record GoalsProfileResponse(double? HeightCm, double? WeightKg, string? ActivityLevel, int? Age);

    [Function("GoalsProfile")]
    public async Task<HttpResponseData> GetProfileAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "goals/profile")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new GoalsProfileResponse(null, null, null, null));
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId, cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(
            request,
            new GoalsProfileResponse(
                person?.HeightCm is > 0 ? person.HeightCm : null,
                person?.CurrentWeightKg,
                person?.ActivityLevel?.ToString(),
                person?.Age));
    }

    public sealed record GoalsProfileSaveRequest(double WeightKg, double HeightCm, string ActivityLevel, int? Age);

    /// <summary>
    /// Lightweight save for the "1. Dime tu estado actual" fields - updates weight/height/
    /// activity and logs a new WeightLog snapshot WITHOUT calling the AI agent, so the user
    /// can persist a stat change (e.g. a new weigh-in) without waiting on a full plan
    /// regeneration.
    /// </summary>
    [Function("GoalsProfileSave")]
    public async Task<HttpResponseData> SaveProfileAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "goals/profile")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "La base de datos no está configurada (falta PersonalAgentDatabase en local.settings.json).",
                HttpStatusCode.ServiceUnavailable);
        }

        GoalsProfileSaveRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<GoalsProfileSaveRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid GoalsProfileSave request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || body.WeightKg <= 0 || body.HeightCm <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Se requieren 'weightKg' y 'heightCm' válidos.", HttpStatusCode.BadRequest);
        }

        var activityLevel = Enum.TryParse<ActivityLevel>(body.ActivityLevel, ignoreCase: true, out var parsedActivity)
            ? parsedActivity
            : Data.ActivityLevel.Sedentary;

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var person = await db.People.FirstAsync(p => p.Id == personId, cancellationToken);
            var weightChanged = person.CurrentWeightKg != body.WeightKg;
            person.HeightCm = body.HeightCm;
            person.CurrentWeightKg = body.WeightKg;
            person.ActivityLevel = activityLevel;
            person.Age = body.Age;

            if (weightChanged)
            {
                db.WeightLogs.Add(new WeightLog { PersonId = personId, WeightKg = body.WeightKg });
            }

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(
                request,
                new GoalsProfileResponse(person.HeightCm, person.CurrentWeightKg, person.ActivityLevel?.ToString(), person.Age));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GoalsProfileSave failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al guardar tus datos.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record GoalPlanRequest(double WeightKg, double HeightCm, string ActivityLevel, string GoalsText, int? Age);

    [Function("GoalsPlanCreate")]
    public async Task<HttpResponseData> CreatePlanAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "goals/plan")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!_goalsAgent.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "GoalsAgent no está configurado (faltan credenciales de Azure OpenAI).", HttpStatusCode.ServiceUnavailable);
        }

        GoalPlanRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<GoalPlanRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid GoalsPlanCreate request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || body.WeightKg <= 0 || body.HeightCm <= 0 || string.IsNullOrWhiteSpace(body.GoalsText))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "Se requieren 'weightKg', 'heightCm' y 'goalsText' válidos.", HttpStatusCode.BadRequest);
        }

        var activityLevel = Enum.TryParse<ActivityLevel>(body.ActivityLevel, ignoreCase: true, out var parsedActivity)
            ? parsedActivity
            : Data.ActivityLevel.Sedentary;

        string rawJson;
        try
        {
            rawJson = await _goalsAgent.GenerateGoalPlanJsonAsync(
                body.WeightKg, body.HeightCm, activityLevel.ToString(), body.GoalsText, body.Age, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GoalsAgent.GenerateGoalPlanJsonAsync failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo generar el plan.", HttpStatusCode.InternalServerError);
        }

        JsonNode? planNode;
        try
        {
            planNode = JsonNode.Parse(StripMarkdownFences(rawJson));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "GoalsAgent returned non-JSON output: {RawJson}", rawJson);
            planNode = null;
        }

        var heightM = body.HeightCm / 100.0;
        var bmi = Math.Round(body.WeightKg / (heightM * heightM), 1);

        int? planId = null;
        if (_dbContextFactory is not null && _personProvider is not null)
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var person = await db.People.FirstAsync(p => p.Id == personId, cancellationToken);
            person.HeightCm = body.HeightCm;
            person.CurrentWeightKg = body.WeightKg;
            person.ActivityLevel = activityLevel;
            person.Age = body.Age;

            db.WeightLogs.Add(new WeightLog { PersonId = personId, WeightKg = body.WeightKg });

            var goalPlan = new GoalPlan
            {
                PersonId = personId,
                WeightKg = body.WeightKg,
                HeightCm = body.HeightCm,
                Bmi = bmi,
                ActivityLevel = activityLevel,
                GoalsText = body.GoalsText,
                RecommendationJson = planNode?.ToJsonString(JsonOptions) ?? rawJson,
            };
            db.GoalPlans.Add(goalPlan);

            if (planNode is JsonObject planObject)
            {
                AddAutoGoalsFromPlan(db, personId, planObject);
            }

            await db.SaveChangesAsync(cancellationToken);
            planId = goalPlan.Id;
        }

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            planId,
            plan = (object?)planNode ?? new { raw = rawJson },
        });
    }

    [Function("GoalsPlanLatest")]
    public async Task<HttpResponseData> GetLatestPlanAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "goals/plan/latest")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new { plan = (object?)null, planId = (int?)null });
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var latest = await db.GoalPlans
            .Where(gp => gp.PersonId == personId)
            .OrderByDescending(gp => gp.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (latest is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new { plan = (object?)null, planId = (int?)null });
        }

        JsonNode? planNode;
        try
        {
            planNode = JsonNode.Parse(latest.RecommendationJson);
        }
        catch (JsonException)
        {
            planNode = null;
        }

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            planId = latest.Id,
            plan = (object?)planNode ?? new { raw = latest.RecommendationJson },
        });
    }

    public sealed record GoalPlanCheckInRequest(
        string? CheckInDate,
        int? StepsWalked,
        bool FollowedNutrition,
        bool FollowedExercise,
        string? Notes);

    public sealed record GoalPlanCheckInResponse(
        int Id,
        string CheckInDate,
        int? StepsWalked,
        bool FollowedNutrition,
        bool FollowedExercise,
        string? Notes);

    [Function("GoalCheckInSave")]
    public async Task<HttpResponseData> SaveCheckInAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "goals/plan/{planId:int}/checkin")]
        HttpRequestData request,
        int planId,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        GoalPlanCheckInRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<GoalPlanCheckInRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid GoalCheckInSave request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        var checkInDate = string.IsNullOrWhiteSpace(body.CheckInDate)
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : DateOnly.Parse(body.CheckInDate);

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var planExists = await db.GoalPlans.AnyAsync(gp => gp.Id == planId && gp.PersonId == personId, cancellationToken);
        if (!planExists)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Plan no encontrado.", HttpStatusCode.NotFound);
        }

        var checkIn = await db.GoalPlanCheckIns.FirstOrDefaultAsync(
            c => c.GoalPlanId == planId && c.CheckInDate == checkInDate, cancellationToken);

        if (checkIn is null)
        {
            checkIn = new GoalPlanCheckIn { GoalPlanId = planId, PersonId = personId, CheckInDate = checkInDate };
            db.GoalPlanCheckIns.Add(checkIn);
        }

        checkIn.StepsWalked = body.StepsWalked;
        checkIn.FollowedNutrition = body.FollowedNutrition;
        checkIn.FollowedExercise = body.FollowedExercise;
        checkIn.Notes = body.Notes;
        checkIn.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new GoalPlanCheckInResponse(
            checkIn.Id,
            checkIn.CheckInDate.ToString("yyyy-MM-dd"),
            checkIn.StepsWalked,
            checkIn.FollowedNutrition,
            checkIn.FollowedExercise,
            checkIn.Notes));
    }

    [Function("GoalCheckInHistory")]
    public async Task<HttpResponseData> GetCheckInHistoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "goals/plan/{planId:int}/checkins")]
        HttpRequestData request,
        int planId,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new { checkIns = Array.Empty<GoalPlanCheckInResponse>() });
        }

        var days = 14;
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        if (int.TryParse(query["days"], out var requestedDays) && requestedDays is > 0 and <= 90)
        {
            days = requestedDays;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

        var checkIns = await db.GoalPlanCheckIns
            .Where(c => c.GoalPlanId == planId && c.CheckInDate >= since)
            .OrderByDescending(c => c.CheckInDate)
            .Select(c => new GoalPlanCheckInResponse(
                c.Id, c.CheckInDate.ToString("yyyy-MM-dd"), c.StepsWalked, c.FollowedNutrition, c.FollowedExercise, c.Notes))
            .ToListAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new { checkIns });
    }

    private static void AddAutoGoalsFromPlan(PersonalAgentDbContext db, int personId, JsonObject planObject)
    {
        if (planObject.TryGetPropertyValue("targetWeightKg", out var targetWeightNode)
            && targetWeightNode?.GetValueKind() == JsonValueKind.Number)
        {
            DateTime? targetDate = null;
            if (planObject.TryGetPropertyValue("estimatedWeeksToGoal", out var weeksNode)
                && weeksNode?.GetValueKind() == JsonValueKind.Number)
            {
                targetDate = DateTime.UtcNow.AddDays(weeksNode.GetValue<double>() * 7);
            }

            db.Goals.Add(new Goal
            {
                PersonId = personId,
                Type = GoalType.Weight,
                Description = "Peso objetivo sugerido por GoalsAgent",
                TargetValue = targetWeightNode.GetValue<double>(),
                TargetDateUtc = targetDate,
            });
        }

        if (planObject.TryGetPropertyValue("dailyCalorieTarget", out var caloriesNode)
            && caloriesNode?.GetValueKind() == JsonValueKind.Number)
        {
            db.Goals.Add(new Goal
            {
                PersonId = personId,
                Type = GoalType.Nutrition,
                Description = "Meta calórica diaria sugerida por GoalsAgent",
                TargetValue = caloriesNode.GetValue<double>(),
            });
        }

        if (planObject["exercisePlan"] is JsonObject exercisePlan
            && exercisePlan.TryGetPropertyValue("daysPerWeek", out var daysNode)
            && daysNode?.GetValueKind() == JsonValueKind.Number
            && exercisePlan.TryGetPropertyValue("minutesPerSession", out var minutesNode)
            && minutesNode?.GetValueKind() == JsonValueKind.Number)
        {
            db.Goals.Add(new Goal
            {
                PersonId = personId,
                Type = GoalType.Exercise,
                Description = "Meta de ejercicio semanal sugerida por GoalsAgent",
                TargetValue = daysNode.GetValue<double>() * minutesNode.GetValue<double>(),
            });
        }
    }

    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        var withoutOpeningFence = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;
        var closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return closingFenceIndex >= 0 ? withoutOpeningFence[..closingFenceIndex].Trim() : withoutOpeningFence.Trim();
    }
}
