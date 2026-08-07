using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalAgent.Common;
using PersonalAgent.Data;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Backs the Ejercicios page's exercise log (pesas, correr, nadar, etc. with duration and
/// calories) - same CRUD shape as WeightFunction, writing to the previously-unused
/// ExerciseLogs table.
/// </summary>
public sealed class ExerciseFunction
{
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly PersonalAgent.Agents.ExerciseAgent? _exerciseAgent;
    private readonly ILogger<ExerciseFunction> _logger;

    public ExerciseFunction(
        ILogger<ExerciseFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null,
        PersonalAgent.Agents.ExerciseAgent? exerciseAgent = null)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
        _exerciseAgent = exerciseAgent;
    }

    public sealed record ExerciseLogDto(int Id, string Description, int DurationMinutes, double? CaloriesBurned, DateTime RecordedAtUtc);

    public sealed record ExerciseHistoryResponse(IReadOnlyList<ExerciseLogDto> Entries, int TotalMinutes);

    [Function("ExerciseQuery")]
    public async Task<HttpResponseData> GetHistoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "exercise")]
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

        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var daysRaw = query["days"];
        var days = int.TryParse(daysRaw, out var parsedDays) && parsedDays > 0 ? parsedDays : 90;
        var sinceUtc = DateTime.UtcNow.Date.AddDays(-days);

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var logs = await db.ExerciseLogs
                .Where(e => e.PersonId == personId && e.RecordedAtUtc >= sinceUtc)
                .OrderByDescending(e => e.RecordedAtUtc)
                .ToListAsync(cancellationToken);

            var entries = logs
                .Select(e => new ExerciseLogDto(e.Id, e.Description, e.DurationMinutes, e.CaloriesBurned, DateTime.SpecifyKind(e.RecordedAtUtc, DateTimeKind.Utc)))
                .ToList();

            var totalMinutes = entries.Sum(e => e.DurationMinutes);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new ExerciseHistoryResponse(entries, totalMinutes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseQuery failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al consultar el historial de ejercicio.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record EstimateExerciseRequest(string Description, int DurationMinutes);

    public sealed record EstimateExerciseResponse(string SuggestedName, double EstimatedCaloriesBurned);

    /// <summary>POST /api/exercise/estimate - preview-only: asks the AI to estimate calories
    /// burned and suggest a name for a free-text activity description, WITHOUT saving
    /// anything. Backs the "Crea tu propio ejercicio" flow on the Ejercicio tab - the
    /// frontend shows this as a preview and only calls ExerciseLogCreate if the user accepts.</summary>
    [Function("ExerciseEstimate")]
    public async Task<HttpResponseData> EstimateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "exercise/estimate")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_exerciseAgent is null || !_exerciseAgent.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "El estimador de ejercicio no está configurado.", HttpStatusCode.ServiceUnavailable);
        }

        EstimateExerciseRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<EstimateExerciseRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid ExerciseEstimate request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Description) || body.DurationMinutes <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Se requiere 'description' y 'durationMinutes' válidos.", HttpStatusCode.BadRequest);
        }

        try
        {
            var estimate = await _exerciseAgent.EstimateAsync(body.Description.Trim(), body.DurationMinutes, cancellationToken);
            return await FunctionResponseFactory.SuccessResponseAsync(
                request, new EstimateExerciseResponse(estimate.SuggestedName, estimate.EstimatedCaloriesBurned));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseEstimate failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo estimar las calorías de este ejercicio.", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>DELETE /api/exercise/catalog/{id} - removes a saved custom exercise from
    /// THIS person's own catalog (does not affect past logged entries, which just lose the
    /// PersonalExerciseId reference via ClientSetNull).</summary>
    [Function("ExerciseCatalogDelete")]
    public async Task<HttpResponseData> DeleteCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "exercise/catalog/{id:int}")]
        HttpRequestData request,
        int id,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var catalogItem = await db.PersonalExercises
                .FirstOrDefaultAsync(pe => pe.Id == id && pe.PersonId == personId, cancellationToken);
            if (catalogItem is null)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "Ejercicio no encontrado.", HttpStatusCode.NotFound);
            }

            db.PersonalExercises.Remove(catalogItem);
            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseCatalogDelete failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo eliminar el ejercicio.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record LogExerciseRequest(string Description, int DurationMinutes, double? CaloriesBurned, string? RecordedAtIso);

    public sealed record SaveCustomExerciseRequest(string Name, int DurationMinutes, double? CaloriesBurned, string? RecordedAtIso);

    public sealed record SaveCustomExerciseResponse(int ExerciseLogId, int PersonalExerciseId);

    /// <summary>POST /api/exercise/custom/save - saves an AI-estimated "crea tu propio
    /// ejercicio" activity to THIS person's own reusable catalog (Data/PersonalExercise.cs -
    /// scoped to PersonId, NOT shared globally like FoodItem) and logs it for today in one
    /// step, only called once the user has explicitly accepted the preview.</summary>
    [Function("ExerciseCustomSave")]
    public async Task<HttpResponseData> SaveCustomAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "exercise/custom/save")]
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

        SaveCustomExerciseRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<SaveCustomExerciseRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid ExerciseCustomSave request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Name) || body.DurationMinutes <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Se requiere 'name' y 'durationMinutes' válidos.", HttpStatusCode.BadRequest);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var catalogItem = await PersonalExerciseCatalogHelper.FindOrCreateAsync(
                db, personId, body.Name.Trim(), body.DurationMinutes, body.CaloriesBurned, cancellationToken);

            var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(body.RecordedAtIso, DateTime.UtcNow);
            var exerciseLog = new ExerciseLog
            {
                PersonId = personId,
                Description = catalogItem.Name,
                DurationMinutes = body.DurationMinutes,
                CaloriesBurned = body.CaloriesBurned,
                RecordedAtUtc = recordedAt,
                PersonalExercise = catalogItem,
            };
            db.ExerciseLogs.Add(exerciseLog);

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new SaveCustomExerciseResponse(exerciseLog.Id, catalogItem.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseCustomSave failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo guardar el ejercicio.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record PersonalExerciseDto(int Id, string Name, int DurationMinutes, double? CaloriesBurned, int TimesLogged);

    /// <summary>GET /api/exercise/catalog - lists THIS person's own saved custom exercises
    /// (Data/PersonalExercise.cs), most-logged first, so they can quickly re-log one without
    /// asking the AI to re-estimate it. Personal per-user, unlike the global FoodItems catalog.</summary>
    [Function("ExerciseCatalogList")]
    public async Task<HttpResponseData> ListCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "exercise/catalog")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var items = await db.PersonalExercises
                .Where(pe => pe.PersonId == personId)
                .OrderByDescending(pe => pe.TimesLogged)
                .ThenBy(pe => pe.Name)
                .Select(pe => new PersonalExerciseDto(pe.Id, pe.Name, pe.DurationMinutes, pe.CaloriesBurned, pe.TimesLogged))
                .ToListAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseCatalogList failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo cargar tu catálogo de ejercicios.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record LogCatalogExerciseRequest(int? DurationMinutes, string? RecordedAtIso);

    public sealed record LogCatalogExerciseResponse(int ExerciseLogId);

    /// <summary>POST /api/exercise/catalog/{id}/log - logs an existing entry from THIS
    /// person's own catalog again (optionally with a different duration, scaling calories
    /// proportionally), no AI re-estimation needed.</summary>
    [Function("ExerciseCatalogLog")]
    public async Task<HttpResponseData> LogCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "exercise/catalog/{id:int}/log")]
        HttpRequestData request,
        int id,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        LogCatalogExerciseRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<LogCatalogExerciseRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid ExerciseCatalogLog request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var catalogItem = await db.PersonalExercises
                .FirstOrDefaultAsync(pe => pe.Id == id && pe.PersonId == personId, cancellationToken);
            if (catalogItem is null)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "Ejercicio no encontrado.", HttpStatusCode.NotFound);
            }

            var durationMinutes = body?.DurationMinutes is > 0 ? body.DurationMinutes.Value : catalogItem.DurationMinutes;
            var caloriesBurned = catalogItem.CaloriesBurned.HasValue && catalogItem.DurationMinutes > 0
                ? catalogItem.CaloriesBurned.Value * durationMinutes / catalogItem.DurationMinutes
                : catalogItem.CaloriesBurned;

            catalogItem.TimesLogged++;

            var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(body?.RecordedAtIso, DateTime.UtcNow);
            var exerciseLog = new ExerciseLog
            {
                PersonId = personId,
                Description = catalogItem.Name,
                DurationMinutes = durationMinutes,
                CaloriesBurned = caloriesBurned,
                RecordedAtUtc = recordedAt,
                PersonalExercise = catalogItem,
            };
            db.ExerciseLogs.Add(exerciseLog);

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new LogCatalogExerciseResponse(exerciseLog.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseCatalogLog failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo registrar el ejercicio.", HttpStatusCode.InternalServerError);
        }
    }

    [Function("ExerciseLogCreate")]
    public async Task<HttpResponseData> LogExerciseAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "exercise")]
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

        LogExerciseRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<LogExerciseRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid ExerciseLogCreate request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Description) || body.DurationMinutes <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Se requiere 'description' y 'durationMinutes' válidos.", HttpStatusCode.BadRequest);
        }

        var recordedAt = DateTime.TryParse(body.RecordedAtIso, out var parsed) ? parsed.ToUniversalTime() : DateTime.UtcNow;

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entry = new ExerciseLog
            {
                PersonId = personId,
                Description = body.Description.Trim(),
                DurationMinutes = body.DurationMinutes,
                CaloriesBurned = body.CaloriesBurned,
                RecordedAtUtc = recordedAt,
            };
            db.ExerciseLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(
                request,
                new ExerciseLogDto(entry.Id, entry.Description, entry.DurationMinutes, entry.CaloriesBurned, DateTime.SpecifyKind(entry.RecordedAtUtc, DateTimeKind.Utc)),
                HttpStatusCode.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseLogCreate failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al guardar el ejercicio.", HttpStatusCode.InternalServerError);
        }
    }

    [Function("ExerciseLogDelete")]
    public async Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "exercise/{id:int}")]
        HttpRequestData request,
        int id,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "La base de datos no está configurada (falta PersonalAgentDatabase en local.settings.json).",
                HttpStatusCode.ServiceUnavailable);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var deletedRows = await db.ExerciseLogs
                .Where(e => e.Id == id && e.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 0)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "No se encontró el registro de ejercicio indicado.", HttpStatusCode.NotFound);
            }

            return await FunctionResponseFactory.SuccessResponseAsync(request, new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExerciseLogDelete failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al borrar el registro de ejercicio.", HttpStatusCode.InternalServerError);
        }
    }
}
