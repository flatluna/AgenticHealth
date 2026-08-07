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
/// Read/reuse endpoints for the GLOBAL food product database (Data/FoodItem.cs) built up by
/// every user scanning nutrition labels (see FoodLabelFunction). Backs the "Productos" page,
/// which lets a user browse products already created by anyone and log one as a meal
/// instantly, without re-scanning/re-taking a photo of the same label.
/// </summary>
public sealed class FoodItemsFunction
{
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly ILogger<FoodItemsFunction> _logger;

    public FoodItemsFunction(
        ILogger<FoodItemsFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
    }

    /// <summary>Handles CORS preflight requests (OPTIONS method) for all endpoints.</summary>
    [Function("FoodItemsOptions")]
    public HttpResponseData OptionsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/items/{*route}")]
        HttpRequestData request)
    {
        return FunctionResponseFactory.PreflightResponseAsync(request);
    }

    public sealed record FoodItemDto(
        int Id,
        string Name,
        string? Brand,
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
        string? IngredientsText,
        int TimesLogged);

    /// <summary>GET /api/foods/items?q= - lists products from the global food database
    /// (optionally filtered by name/brand), most-logged first, for the "Productos" page.</summary>
    [Function("FoodItemsList")]
    public async Task<HttpResponseData> ListAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/items")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var search = query["q"]?.Trim();

        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var itemsQuery = db.FoodItems.AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = $"%{search}%";
                itemsQuery = itemsQuery.Where(f => EF.Functions.Like(f.Name, pattern) || (f.Brand != null && EF.Functions.Like(f.Brand, pattern)));
            }

            var items = await itemsQuery
                .OrderByDescending(f => f.TimesLogged)
                .ThenBy(f => f.Name)
                .Take(200)
                .Select(f => new FoodItemDto(
                    f.Id,
                    f.Name,
                    f.Brand,
                    f.ServingSize,
                    f.Calories,
                    f.ProteinGrams,
                    f.CarbsGrams,
                    f.FatGrams,
                    f.SaturatedFatGrams,
                    f.SugarGrams,
                    f.FiberGrams,
                    f.SodiumMilligrams,
                    f.PotassiumMilligrams,
                    f.CalciumMilligrams,
                    f.IronMilligrams,
                    f.MagnesiumMilligrams,
                    f.VitaminAMicrograms,
                    f.IngredientsText,
                    f.TimesLogged))
                .ToListAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FoodItemsList failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo cargar la lista de productos.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record LogFoodItemRequest(string MealType, string? ConsumedAtIso, double? Quantity = null);

    public sealed record LogFoodItemResponse(int MealLogId);

    /// <summary>POST /api/foods/items/{id}/log - logs an existing global product as a meal
    /// for the calling person, no photo/re-extraction needed since the nutrition data is
    /// already stored.</summary>
    [Function("FoodItemsLog")]
    public async Task<HttpResponseData> LogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/items/{id:int}/log")]
        HttpRequestData request,
        int id,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        LogFoodItemRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<LogFoodItemRequest>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo JSON inválido.", HttpStatusCode.BadRequest);
        }

        var parsedMealType = Enum.TryParse<MealType>(body?.MealType, ignoreCase: true, out var mt) ? mt : MealType.Snack;
        var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(body?.ConsumedAtIso, DateTime.UtcNow);
        var quantity = body?.Quantity is > 0 ? body.Quantity.Value : 1.0;
        double? Scale(double? value) => value.HasValue ? value.Value * quantity : null;
        var quantityLabel = quantity == 1 ? string.Empty : $" (x{quantity:0.##} porciones)";

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var foodItem = await db.FoodItems.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (foodItem is null)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "Producto no encontrado.", HttpStatusCode.NotFound);
            }

            foodItem.TimesLogged++;

            var baseName = string.IsNullOrWhiteSpace(foodItem.Brand) ? foodItem.Name : $"{foodItem.Name} ({foodItem.Brand})";
            var mealLog = new MealLog
            {
                PersonId = personId,
                MealType = parsedMealType,
                Description = $"{baseName}{quantityLabel}",
                ServingSize = foodItem.ServingSize,
                Calories = Scale(foodItem.Calories),
                ProteinGrams = Scale(foodItem.ProteinGrams),
                CarbsGrams = Scale(foodItem.CarbsGrams),
                FatGrams = Scale(foodItem.FatGrams),
                SaturatedFatGrams = Scale(foodItem.SaturatedFatGrams),
                SugarGrams = Scale(foodItem.SugarGrams),
                FiberGrams = Scale(foodItem.FiberGrams),
                SodiumMilligrams = Scale(foodItem.SodiumMilligrams),
                PotassiumMilligrams = Scale(foodItem.PotassiumMilligrams),
                CalciumMilligrams = Scale(foodItem.CalciumMilligrams),
                IronMilligrams = Scale(foodItem.IronMilligrams),
                MagnesiumMilligrams = Scale(foodItem.MagnesiumMilligrams),
                VitaminAMicrograms = Scale(foodItem.VitaminAMicrograms),
                SourceBreakdown = $"{baseName}{quantityLabel}: {Scale(foodItem.Calories)?.ToString("0") ?? "?"} kcal (producto guardado)",
                RecordedAtUtc = recordedAt,
                FoodItem = foodItem,
            };
            db.MealLogs.Add(mealLog);

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new LogFoodItemResponse(mealLog.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FoodItemsLog failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo registrar el producto.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record SavePersonalFoodItemRequest(
        string Name,
        string? Description,
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
        double? VitaminAMicrograms);

    public sealed record SavePersonalFoodItemResponse(int Id);

    /// <summary>POST /api/foods/personal/save - saves a computed nutrition breakdown (ej.
    /// from the chat's "Guardar en mi catálogo" button) into the calling person's OWN
    /// reusable food catalog (Data/PersonalFoodItem.cs) - find-or-create by name, so saving
    /// the same item again just refreshes its numbers instead of duplicating it.</summary>
    [Function("PersonalFoodSave")]
    public async Task<HttpResponseData> SavePersonalAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/personal/save")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        SavePersonalFoodItemRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<SavePersonalFoodItemRequest>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo JSON inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta el nombre del alimento.", HttpStatusCode.BadRequest);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var item = await PersonalFoodCatalogHelper.FindOrCreateAsync(
                db, personId, body.Name, body.Description, body.ServingSize, body.Calories, body.ProteinGrams, body.CarbsGrams,
                body.FatGrams, body.SaturatedFatGrams, body.SugarGrams, body.FiberGrams, body.SodiumMilligrams,
                body.PotassiumMilligrams, body.CalciumMilligrams, body.IronMilligrams, body.MagnesiumMilligrams,
                body.VitaminAMicrograms, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new SavePersonalFoodItemResponse(item.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PersonalFoodSave failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo guardar en tu catálogo personal.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record PersonalFoodItemDto(
        int Id,
        string Name,
        string? Description,
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
        int TimesLogged);

    /// <summary>GET /api/foods/personal - lists THIS person's own saved catalog entries
    /// (Data/PersonalFoodItem.cs), most-saved/logged first - backs a "Mi catálogo" view so the
    /// user can see what "Guardar en mi catálogo" has stored for them.</summary>
    [Function("PersonalFoodItemsList")]
    public async Task<HttpResponseData> ListPersonalAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "foods/personal")]
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

            var items = await db.PersonalFoodItems
                .Where(pf => pf.PersonId == personId)
                .OrderByDescending(pf => pf.TimesLogged)
                .ThenBy(pf => pf.Name)
                .Select(pf => new PersonalFoodItemDto(
                    pf.Id,
                    pf.Name,
                    pf.Description,
                    pf.ServingSize,
                    pf.Calories,
                    pf.ProteinGrams,
                    pf.CarbsGrams,
                    pf.FatGrams,
                    pf.SaturatedFatGrams,
                    pf.SugarGrams,
                    pf.FiberGrams,
                    pf.SodiumMilligrams,
                    pf.PotassiumMilligrams,
                    pf.CalciumMilligrams,
                    pf.IronMilligrams,
                    pf.MagnesiumMilligrams,
                    pf.VitaminAMicrograms,
                    pf.TimesLogged))
                .ToListAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PersonalFoodItemsList failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo cargar tu catálogo personal.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record LogPersonalFoodItemRequest(string MealType, string? ConsumedAtIso, double? Quantity = null);

    public sealed record LogPersonalFoodItemResponse(int MealLogId);

    /// <summary>POST /api/foods/personal/{id}/log - logs an existing entry from THIS
    /// person's own catalog (Data/PersonalFoodItem.cs) as a meal, no re-computation needed
    /// since the nutrition data is already stored - backs the "Adicionar" action in the "Mi
    /// catálogo" tab.</summary>
    [Function("PersonalFoodItemsLog")]
    public async Task<HttpResponseData> LogPersonalAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/personal/{id:int}/log")]
        HttpRequestData request,
        int id,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        LogPersonalFoodItemRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<LogPersonalFoodItemRequest>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo JSON inválido.", HttpStatusCode.BadRequest);
        }

        try
        {
            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var mealLog = await PersonalFoodCatalogHelper.LogExistingAsync(
                db, personId, id, body?.MealType, body?.ConsumedAtIso, body?.Quantity, cancellationToken);
            if (mealLog is null)
            {
                return await FunctionResponseFactory.ErrorResponseAsync(request, "Alimento no encontrado en tu catálogo.", HttpStatusCode.NotFound);
            }

            await db.SaveChangesAsync(cancellationToken);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new LogPersonalFoodItemResponse(mealLog.Id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PersonalFoodItemsLog failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo registrar el alimento.", HttpStatusCode.InternalServerError);
        }
    }
}

