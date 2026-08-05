using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalAgent.Agents;
using PersonalAgent.Common;
using PersonalAgent.Data;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Executes the Realtime function-calling tools offered to the voice mode session (see
/// VoiceChatSessionFunction.BuildTools). The Realtime API itself can't run business logic -
/// when the model calls a tool, Azure sends the call to the BROWSER over the WebRTC data
/// channel, and the browser calls these plain REST endpoints to actually run it, then
/// reports the JSON result back over the data channel as a function_call_output.
///
/// "log_meal" here mirrors DietAgent.LogMealAsync's parameters/behavior 1:1 so voice and
/// text chat write the exact same MealLog schema. "ask_health_advisor" forwards to the
/// same AdvisorAgent the text chat uses, so voice mode gets the exact same real-history
/// grounding (meals/exercise/weight/goals) instead of having zero context.
/// </summary>
public sealed class VoiceToolsFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly BingFoodSearchProvider _bingFoodSearchProvider;
    private readonly AdvisorAgent _advisorAgent;
    private readonly ILogger<VoiceToolsFunction> _logger;

    public VoiceToolsFunction(
        BingFoodSearchProvider bingFoodSearchProvider,
        AdvisorAgent advisorAgent,
        ILogger<VoiceToolsFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _bingFoodSearchProvider = bingFoodSearchProvider;
        _advisorAgent = advisorAgent;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
    }

    public sealed record LogMealRequest(
        string MealType,
        string Description,
        string? ServingSize,
        double? Calories,
        double? ProteinGrams,
        double? CarbsGrams,
        double? FatGrams,
        double? SaturatedFatGrams,
        double? SugarGrams,
        double? FiberGrams,
        double? SodiumMilligrams,
        double? PotassiumMilligrams,
        double? CalciumMilligrams,
        double? IronMilligrams,
        double? MagnesiumMilligrams,
        double? VitaminAMicrograms,
        string? ConsumedAtIso,
        string? SourceBreakdown);

    [Function("VoiceToolLogMeal")]
    public async Task<HttpResponseData> LogMealAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/log-meal")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        LogMealRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<LogMealRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Description))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la descripción de la comida.", HttpStatusCode.BadRequest);
        }

        var parsedMealType = Enum.TryParse<MealType>(body.MealType, ignoreCase: true, out var mt) ? mt : MealType.Snack;
        var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(body.ConsumedAtIso, DateTime.UtcNow);

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MealLogs.Add(new MealLog
        {
            PersonId = personId,
            MealType = parsedMealType,
            Description = body.Description,
            ServingSize = body.ServingSize,
            Calories = body.Calories,
            ProteinGrams = body.ProteinGrams,
            CarbsGrams = body.CarbsGrams,
            FatGrams = body.FatGrams,
            SaturatedFatGrams = body.SaturatedFatGrams,
            SugarGrams = body.SugarGrams,
            FiberGrams = body.FiberGrams,
            SodiumMilligrams = body.SodiumMilligrams,
            PotassiumMilligrams = body.PotassiumMilligrams,
            CalciumMilligrams = body.CalciumMilligrams,
            IronMilligrams = body.IronMilligrams,
            MagnesiumMilligrams = body.MagnesiumMilligrams,
            VitaminAMicrograms = body.VitaminAMicrograms,
            RecordedAtUtc = recordedAt,
            SourceBreakdown = string.IsNullOrWhiteSpace(body.SourceBreakdown)
                ? $"{body.Description}: {body.Calories?.ToString("0") ?? "?"} kcal (fuente no especificada)"
                : body.SourceBreakdown,
        });
        await db.SaveChangesAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            confirmation = $"Registrado: {body.Description} ({parsedMealType}, {body.Calories?.ToString("0") ?? "?"} kcal) a las {TimeZoneInfo.ConvertTimeFromUtc(recordedAt, MealTimeHelper.Central):HH:mm}.",
        });
    }

    public sealed record SearchFoodRequest(string FoodDescription);

    [Function("VoiceToolSearchFood")]
    public async Task<HttpResponseData> SearchFoodAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/search-food")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        SearchFoodRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SearchFoodRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.FoodDescription))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la descripción del alimento.", HttpStatusCode.BadRequest);
        }

        if (!_bingFoodSearchProvider.IsConfigured)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "Búsqueda no disponible; estima los valores nutricionales con tu propio conocimiento.",
            });
        }

        try
        {
            var json = await _bingFoodSearchProvider.SearchFoodNutritionJsonAsync(body.FoodDescription, cancellationToken);
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = json ?? "No se encontraron resultados; estima los valores con tu propio conocimiento.",
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VoiceToolSearchFood failed");
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "La búsqueda falló; estima los valores nutricionales con tu propio conocimiento.",
            });
        }
    }

    public sealed record GetRecentMealsRequest(int? DaysBack);

    [Function("VoiceToolGetRecentMeals")]
    public async Task<HttpResponseData> GetRecentMealsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/get-recent-meals")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "No se pudo consultar el historial: la base de datos no está configurada.",
            });
        }

        GetRecentMealsRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<GetRecentMealsRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            body = new GetRecentMealsRequest(null);
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        var summary = await MealHistoryHelper.GetRecentMealsSummaryAsync(_dbContextFactory, personId, body?.DaysBack, cancellationToken);
        return await FunctionResponseFactory.SuccessResponseAsync(request, new { result = summary });
    }

    public sealed record LogExerciseRequest(string Description, int DurationMinutes, double? CaloriesBurned, string? RecordedAtIso);

    [Function("VoiceToolLogExercise")]
    public async Task<HttpResponseData> LogExerciseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/log-exercise")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        LogExerciseRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<LogExerciseRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Description) || body.DurationMinutes <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la descripción o la duración del ejercicio.", HttpStatusCode.BadRequest);
        }

        var recordedAt = DateTime.TryParse(body.RecordedAtIso, out var parsed) ? parsed.ToUniversalTime() : DateTime.UtcNow;
        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.ExerciseLogs.Add(new ExerciseLog
        {
            PersonId = personId,
            Description = body.Description.Trim(),
            DurationMinutes = body.DurationMinutes,
            CaloriesBurned = body.CaloriesBurned,
            RecordedAtUtc = recordedAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            confirmation = $"Registrado: {body.Description} ({body.DurationMinutes} min" +
                (body.CaloriesBurned is { } kcal ? $", {kcal:0} kcal quemadas" : string.Empty) + ").",
        });
    }

    public sealed record DeleteMealRequest(int MealId);

    [Function("VoiceToolDeleteMeal")]
    public async Task<HttpResponseData> DeleteMealAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/delete-meal")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        DeleteMealRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<DeleteMealRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || body.MealId <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta el identificador de la comida.", HttpStatusCode.BadRequest);
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var deletedRows = await db.MealLogs
            .Where(m => m.Id == body.MealId && m.PersonId == personId)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedRows == 0)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                confirmation = "No encontré esa comida registrada; puede que ya se haya borrado.",
            });
        }

        return await FunctionResponseFactory.SuccessResponseAsync(request, new { confirmation = "Listo, borré ese registro de comida." });
    }

    public sealed record AskAdvisorRequest(string Question, string? UserName);

    [Function("VoiceToolAskAdvisor")]
    public async Task<HttpResponseData> AskAdvisorAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/ask-advisor")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        AskAdvisorRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<AskAdvisorRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Question))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la pregunta.", HttpStatusCode.BadRequest);
        }

        if (!_advisorAgent.IsConfigured)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "El asesor de historial no está disponible en este momento.",
            });
        }

        try
        {
            var answer = await _advisorAgent.AskAsync(body.Question, body.UserName, cancellationToken);
            return await FunctionResponseFactory.SuccessResponseAsync(request, new { result = answer });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VoiceToolAskAdvisor failed");
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "No pude consultar tu historial en este momento.",
            });
        }
    }
}
