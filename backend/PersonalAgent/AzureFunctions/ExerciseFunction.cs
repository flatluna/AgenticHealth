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
    private readonly ILogger<ExerciseFunction> _logger;

    public ExerciseFunction(
        ILogger<ExerciseFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
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
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
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

    public sealed record LogExerciseRequest(string Description, int DurationMinutes, double? CaloriesBurned, string? RecordedAtIso);

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
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
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
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);

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
