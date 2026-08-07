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
    private readonly EdamamFoodSearchProvider _edamamFoodSearchProvider;
    private readonly AdvisorAgent _advisorAgent;
    private readonly ILogger<VoiceToolsFunction> _logger;

    public VoiceToolsFunction(
        BingFoodSearchProvider bingFoodSearchProvider,
        EdamamFoodSearchProvider edamamFoodSearchProvider,
        AdvisorAgent advisorAgent,
        ILogger<VoiceToolsFunction> logger,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _bingFoodSearchProvider = bingFoodSearchProvider;
        _edamamFoodSearchProvider = edamamFoodSearchProvider;
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

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

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

    public sealed record SearchFoodRequest(string[] FoodDescriptions);

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

        if (body is null || body.FoodDescriptions is null || body.FoodDescriptions.Length == 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la descripción del alimento.", HttpStatusCode.BadRequest);
        }

        if (!_edamamFoodSearchProvider.IsConfigured && !_bingFoodSearchProvider.IsConfigured)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "Búsqueda no disponible; estima los valores nutricionales con tu propio conocimiento.",
            });
        }

        try
        {
            // Same priority/shape as DietAgent's text-chat flow: try Edamam's structured API
            // first for ALL components in one call (fast, 1-3s), then fall back to Bing
            // Grounding (slower) ONLY for the components Edamam couldn't resolve - never the
            // whole compound description as one query, since Edamam's NLP parser expects
            // concise single-food English phrases, not descriptive Spanish sentences (it
            // silently mis-parses those into near-zero/garbage nutrition values).
            var items = new JsonNode?[body.FoodDescriptions.Length];

            if (_edamamFoodSearchProvider.IsConfigured)
            {
                var edamamJson = await _edamamFoodSearchProvider.SearchFoodsNutritionJsonAsync(body.FoodDescriptions, cancellationToken);
                if (edamamJson is not null && JsonNode.Parse(edamamJson) is JsonArray edamamArray)
                {
                    var length = Math.Min(edamamArray.Count, items.Length);
                    for (var i = 0; i < length; i++)
                    {
                        if (edamamArray[i] is JsonObject obj && obj["calories"] is not null)
                        {
                            items[i] = obj.DeepClone();
                        }
                    }
                }
            }

            var unmatchedIndexes = Enumerable.Range(0, items.Length).Where(i => items[i] is null).ToArray();
            if (unmatchedIndexes.Length > 0 && _bingFoodSearchProvider.IsConfigured)
            {
                var unmatchedDescriptions = unmatchedIndexes.Select(i => body.FoodDescriptions[i]).ToArray();
                var bingJson = await _bingFoodSearchProvider.SearchFoodsNutritionJsonAsync(unmatchedDescriptions, cancellationToken);
                if (bingJson is not null && JsonNode.Parse(bingJson) is JsonArray bingArray)
                {
                    var length = Math.Min(bingArray.Count, unmatchedIndexes.Length);
                    for (var i = 0; i < length; i++)
                    {
                        items[unmatchedIndexes[i]] = bingArray[i]?.DeepClone();
                    }
                }
            }

            var resultArray = new JsonArray();
            for (var i = 0; i < items.Length; i++)
            {
                resultArray.Add(items[i]?.DeepClone() ?? new JsonObject { ["query"] = body.FoodDescriptions[i], ["calories"] = null });
            }

            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = resultArray.ToJsonString(JsonOptions),
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

    public sealed record SearchFoodCatalogRequest(string FoodDescription);

    [Function("VoiceToolSearchFoodCatalog")]
    public async Task<HttpResponseData> SearchFoodCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/search-food-catalog")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "El catálogo de productos no está disponible.",
            });
        }

        SearchFoodCatalogRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SearchFoodCatalogRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.FoodDescription))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la descripción del alimento.", HttpStatusCode.BadRequest);
        }

        var queryWords = FoodCatalogMatcher.SignificantWords(body.FoodDescription);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var allItems = await db.FoodItems.ToListAsync(cancellationToken);
        var matches = FoodCatalogMatcher.RankCandidates(allItems, queryWords, f => $"{f.Name} {f.Brand}", f => f.TimesLogged, take: 5)
            .Select(f => new
            {
                name = f.Name,
                brand = f.Brand,
                servingSize = f.ServingSize,
                calories = f.Calories,
                proteinGrams = f.ProteinGrams,
                carbsGrams = f.CarbsGrams,
                fatGrams = f.FatGrams,
                saturatedFatGrams = f.SaturatedFatGrams,
                sugarGrams = f.SugarGrams,
                fiberGrams = f.FiberGrams,
                sodiumMilligrams = f.SodiumMilligrams,
                potassiumMilligrams = f.PotassiumMilligrams,
                calciumMilligrams = f.CalciumMilligrams,
                ironMilligrams = f.IronMilligrams,
                magnesiumMilligrams = f.MagnesiumMilligrams,
                vitaminAMicrograms = f.VitaminAMicrograms,
                timesLogged = f.TimesLogged,
            })
            .ToList();

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            result = matches.Count == 0
                ? "No encontré ese producto en nuestro catálogo global; búscalo en la web con search_food_nutrition."
                : JsonSerializer.Serialize(matches, JsonOptions),
        });
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

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
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
        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

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

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);

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

    public sealed record SearchPersonalCatalogRequest(string FoodDescription);

    /// <summary>Executes the "search_personal_catalog" Realtime tool - looks up THIS
    /// person's own saved catalog (Data/PersonalFoodItem.cs, same one behind the "Mi
    /// catálogo" tab) by name/description text, so a previously-saved item (ej. "mi
    /// ensalada de siempre") can be reused instead of re-searching the web.</summary>
    [Function("VoiceToolSearchPersonalCatalog")]
    public async Task<HttpResponseData> SearchPersonalCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/search-personal-catalog")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "No se pudo consultar tu catálogo personal: la base de datos no está configurada.",
            });
        }

        SearchPersonalCatalogRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SearchPersonalCatalogRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.FoodDescription))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta la descripción del alimento.", HttpStatusCode.BadRequest);
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var matches = await PersonalFoodCatalogHelper.RankByWordOverlapAsync(db, personId, body.FoodDescription, take: 5, cancellationToken);

        if (matches.Count == 0)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                result = "No encontré nada parecido en tu catálogo personal; búscalo en la web con search_food_nutrition.",
            });
        }

        var summaries = matches.Select(m => new
        {
            personalFoodItemId = m.Id,
            name = m.Name,
            description = m.Description,
            servingSize = m.ServingSize,
            calories = m.Calories,
            proteinGrams = m.ProteinGrams,
            carbsGrams = m.CarbsGrams,
            fatGrams = m.FatGrams,
            saturatedFatGrams = m.SaturatedFatGrams,
            sugarGrams = m.SugarGrams,
            fiberGrams = m.FiberGrams,
            sodiumMilligrams = m.SodiumMilligrams,
            potassiumMilligrams = m.PotassiumMilligrams,
            calciumMilligrams = m.CalciumMilligrams,
            ironMilligrams = m.IronMilligrams,
            magnesiumMilligrams = m.MagnesiumMilligrams,
            vitaminAMicrograms = m.VitaminAMicrograms,
            timesLogged = m.TimesLogged,
        });
        return await FunctionResponseFactory.SuccessResponseAsync(request, new { result = JsonSerializer.Serialize(summaries, JsonOptions) });
    }

    public sealed record LogPersonalCatalogItemRequest(int PersonalFoodItemId, string MealType, string? ConsumedAtIso, double? Quantity = null);

    /// <summary>Executes the "log_personal_catalog_item" Realtime tool - logs an item
    /// already found via "search_personal_catalog" as a meal, reusing its stored nutrition
    /// data (same endpoint/logic as the "Adicionar" button in the "Mi catálogo" tab).</summary>
    [Function("VoiceToolLogPersonalCatalogItem")]
    public async Task<HttpResponseData> LogPersonalCatalogItemAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/log-personal-catalog-item")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        LogPersonalCatalogItemRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<LogPersonalCatalogItemRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || body.PersonalFoodItemId <= 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta el identificador del alimento.", HttpStatusCode.BadRequest);
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mealLog = await PersonalFoodCatalogHelper.LogExistingAsync(
            db, personId, body.PersonalFoodItemId, body.MealType, body.ConsumedAtIso, body.Quantity, cancellationToken);

        if (mealLog is null)
        {
            return await FunctionResponseFactory.SuccessResponseAsync(request, new
            {
                confirmation = "No encontré ese alimento en tu catálogo personal.",
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            confirmation = $"Registrado desde tu catálogo: {mealLog.Description} ({mealLog.Calories?.ToString("0") ?? "?"} kcal).",
        });
    }

    public sealed record SaveToPersonalCatalogRequest(
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

    /// <summary>Executes the "save_to_personal_catalog" Realtime tool - saves a just-logged
    /// (or just-discussed) food into THIS person's own reusable catalog (same find-or-create
    /// logic as the chat's "Guardar en mi catálogo" button), so it can be found later via
    /// "search_personal_catalog" instead of re-searching the web.</summary>
    [Function("VoiceToolSaveToPersonalCatalog")]
    public async Task<HttpResponseData> SaveToPersonalCatalogAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/tools/save-to-personal-catalog")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        SaveToPersonalCatalogRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<SaveToPersonalCatalogRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de solicitud inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta el nombre del alimento.", HttpStatusCode.BadRequest);
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var item = await PersonalFoodCatalogHelper.FindOrCreateAsync(
            db, personId, body.Name, body.Description, body.ServingSize, body.Calories, body.ProteinGrams, body.CarbsGrams,
            body.FatGrams, body.SaturatedFatGrams, body.SugarGrams, body.FiberGrams, body.SodiumMilligrams,
            body.PotassiumMilligrams, body.CalciumMilligrams, body.IronMilligrams, body.MagnesiumMilligrams,
            body.VitaminAMicrograms, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            confirmation = $"Guardado en tu catálogo personal: {item.Name}.",
        });
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
            var azureObjectId = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
            var answer = await _advisorAgent.AskAsync(body.Question, azureObjectId, body.UserName, cancellationToken);
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
