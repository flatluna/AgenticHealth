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
/// Backs the Peso page: a standalone weight history/tracker, separate from the Objetivos
/// (goal plan) flow. WeightLog rows created here are the same table GoalsFunction writes
/// to when a plan is generated - both feed the same history, they just have two entry
/// points (manual logging here vs. an automatic snapshot when generating a plan).
/// </summary>
public sealed class WeightFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly ILogger<WeightFunction> _logger;

    public WeightFunction(
        ILogger<WeightFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
    }

    public sealed record WeightLogDto(int Id, double WeightKg, DateTime RecordedAtUtc);

    public sealed record WeightHistoryResponse(IReadOnlyList<WeightLogDto> Entries, double? LatestWeightKg, double? ChangeKg);

    [Function("WeightQuery")]
    public async Task<HttpResponseData> GetHistoryAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "weight")]
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

            var logs = await db.WeightLogs
                .Where(w => w.PersonId == personId && w.RecordedAtUtc >= sinceUtc)
                .OrderBy(w => w.RecordedAtUtc)
                .ToListAsync(cancellationToken);

            var entries = logs
                .Select(w => new WeightLogDto(w.Id, w.WeightKg, DateTime.SpecifyKind(w.RecordedAtUtc, DateTimeKind.Utc)))
                .ToList();

            var latest = entries.Count > 0 ? entries[^1].WeightKg : (double?)null;
            var change = entries.Count > 1 ? Math.Round(entries[^1].WeightKg - entries[0].WeightKg, 1) : (double?)null;

            return await FunctionResponseFactory.SuccessResponseAsync(request, new WeightHistoryResponse(entries, latest, change));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeightQuery failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al consultar el historial de peso.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record LogWeightRequest(double WeightKg, string? RecordedAtIso);

    [Function("WeightLogCreate")]
    public async Task<HttpResponseData> LogWeightAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "weight")]
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

        LogWeightRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<LogWeightRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid WeightLogCreate request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || body.WeightKg <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Se requiere un 'weightKg' válido.", HttpStatusCode.BadRequest);
        }

        var recordedAt = DateTime.TryParse(body.RecordedAtIso, out var parsed) ? parsed.ToUniversalTime() : DateTime.UtcNow;

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var entry = new WeightLog { PersonId = personId, WeightKg = body.WeightKg, RecordedAtUtc = recordedAt };
            db.WeightLogs.Add(entry);

            var person = await db.People.FirstAsync(p => p.Id == personId, cancellationToken);
            var latestLog = await db.WeightLogs
                .Where(w => w.PersonId == personId)
                .OrderByDescending(w => w.RecordedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (latestLog is null || recordedAt >= latestLog.RecordedAtUtc)
            {
                person.CurrentWeightKg = body.WeightKg;
            }

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(
                request,
                new WeightLogDto(entry.Id, entry.WeightKg, DateTime.SpecifyKind(entry.RecordedAtUtc, DateTimeKind.Utc)),
                HttpStatusCode.Created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeightLogCreate failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al guardar el peso.", HttpStatusCode.InternalServerError);
        }
    }

    [Function("WeightLogDelete")]
    public async Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "weight/{id:int}")]
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
            var deletedRows = await db.WeightLogs
                .Where(w => w.Id == id && w.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 0)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "No se encontró el registro de peso indicado.", HttpStatusCode.NotFound);
            }

            return await FunctionResponseFactory.SuccessResponseAsync(request, new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WeightLogDelete failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al borrar el registro de peso.", HttpStatusCode.InternalServerError);
        }
    }
}
