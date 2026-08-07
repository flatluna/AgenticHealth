using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PersonalAgent.Common;
using PersonalAgent.Data;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Read-only query endpoint for the meals the default person has logged, used by the
/// frontend's "Alimentos" calendar page (day/week/month views with nutritional totals).
/// </summary>
public sealed class MealsQueryFunction
{
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly ILogger<MealsQueryFunction> _logger;

    public MealsQueryFunction(
        ILogger<MealsQueryFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
    }

    public sealed record MealDto(
        int Id,
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
        string? SourceBreakdown,
        DateTime RecordedAtUtc);

    public sealed record NutritionTotals(
        double Calories,
        double ProteinGrams,
        double CarbsGrams,
        double FatGrams,
        double SugarGrams,
        double FiberGrams,
        double SodiumMilligrams,
        double PotassiumMilligrams);

    public sealed record MealsResponse(IReadOnlyList<MealDto> Meals, NutritionTotals Totals);

    [Function("MealsQuery")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "meals")]
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
        var fromRaw = query["from"];
        var toRaw = query["to"];

        if (!DateTime.TryParse(fromRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var from) ||
            !DateTime.TryParse(toRaw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var to))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "Parámetros 'from' y 'to' son obligatorios (fechas ISO 8601 válidas).", HttpStatusCode.BadRequest);
        }

        // 'from' is the inclusive lower bound and 'to' the EXCLUSIVE upper bound, both sent
        // by the frontend as precise UTC instants (already accounting for the caller's local
        // timezone), not as plain calendar dates - avoids excluding meals recorded late in
        // the evening local time that land on the next UTC calendar day.
        var fromUtc = DateTime.SpecifyKind(from.ToUniversalTime(), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.ToUniversalTime(), DateTimeKind.Utc);

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var mealEntities = await db.MealLogs
                .Where(m => m.PersonId == personId && m.RecordedAtUtc >= fromUtc && m.RecordedAtUtc < toUtc)
                .OrderBy(m => m.RecordedAtUtc)
                .ToListAsync(cancellationToken);

            // SQL Server's datetime2 doesn't persist DateTimeKind, so EF Core materializes
            // RecordedAtUtc as Kind=Unspecified - System.Text.Json then serializes it WITHOUT
            // a trailing 'Z', which JS `new Date(...)` parses as LOCAL time instead of UTC.
            // Explicitly re-stamp Kind=Utc here (done in-memory, after ToListAsync, since
            // DateTime.SpecifyKind isn't translatable to SQL) so the JSON is unambiguous.
            var meals = mealEntities
                .Select(m => new MealDto(
                    m.Id,
                    m.MealType.ToString(),
                    m.Description,
                    m.ServingSize,
                    m.Calories,
                    m.ProteinGrams,
                    m.CarbsGrams,
                    m.FatGrams,
                    m.SaturatedFatGrams,
                    m.SugarGrams,
                    m.FiberGrams,
                    m.SodiumMilligrams,
                    m.PotassiumMilligrams,
                    m.CalciumMilligrams,
                    m.IronMilligrams,
                    m.MagnesiumMilligrams,
                    m.VitaminAMicrograms,
                    m.SourceBreakdown,
                    DateTime.SpecifyKind(m.RecordedAtUtc, DateTimeKind.Utc)))
                .ToList();

            var totals = new NutritionTotals(
                meals.Sum(m => m.Calories ?? 0),
                meals.Sum(m => m.ProteinGrams ?? 0),
                meals.Sum(m => m.CarbsGrams ?? 0),
                meals.Sum(m => m.FatGrams ?? 0),
                meals.Sum(m => m.SugarGrams ?? 0),
                meals.Sum(m => m.FiberGrams ?? 0),
                meals.Sum(m => m.SodiumMilligrams ?? 0),
                meals.Sum(m => m.PotassiumMilligrams ?? 0));

            return await FunctionResponseFactory.SuccessResponseAsync(request, new MealsResponse(meals, totals));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MealsQuery failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al consultar las comidas.", HttpStatusCode.InternalServerError);
        }
    }

    [Function("MealsDelete")]
    public async Task<HttpResponseData> DeleteAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "meals/{id:int}")]
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
            var deletedRows = await db.MealLogs
                .Where(m => m.Id == id && m.PersonId == personId)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedRows == 0)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "No se encontró la comida indicada.", HttpStatusCode.NotFound);
            }

            return await FunctionResponseFactory.SuccessResponseAsync(request, new { deleted = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MealsDelete failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al borrar la comida.", HttpStatusCode.InternalServerError);
        }
    }
}
