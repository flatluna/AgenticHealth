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
/// Backs the "escanear etiqueta" feature on the Nutrición page: upload a photo of a food's
/// nutrition label, extract its info with a vision agent, let the user review/confirm it,
/// then log it as a meal AND store it in the global FoodItems table (shared by every user -
/// see Data/FoodItem.cs) so the same product doesn't need re-scanning/re-extracting next time.
/// </summary>
public sealed class FoodLabelFunction
{
    private const long MaxImageBytes = 10 * 1024 * 1024; // 10 MB

    private readonly FoodLabelExtractionAgent? _extractionAgent;
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly ILogger<FoodLabelFunction> _logger;

    public FoodLabelFunction(
        ILogger<FoodLabelFunction> logger,
        FoodLabelExtractionAgent? extractionAgent = null,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _logger = logger;
        _extractionAgent = extractionAgent;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;
    }

    /// <summary>POST /api/foods/label/extract - raw image bytes in the body, Content-Type
    /// header identifies the format (same "no multipart parsing" convention used elsewhere
    /// for image uploads). Returns the AI-proposed nutrition data for the user to review -
    /// nothing is saved/logged yet, that only happens via FoodLabelSave once confirmed.</summary>
    [Function("FoodLabelExtract")]
    public async Task<HttpResponseData> ExtractAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/label/extract")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_extractionAgent is null || !_extractionAgent.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "El agente de extracción de etiquetas no está configurado en el backend (faltan credenciales de Azure OpenAI).",
                HttpStatusCode.ServiceUnavailable);
        }

        var contentType = request.Headers.TryGetValues("Content-Type", out var values) ? values.FirstOrDefault() : null;
        if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "El archivo debe ser una imagen (image/png, image/jpeg, etc.).", HttpStatusCode.BadRequest);
        }

        using var memoryStream = new MemoryStream();
        await request.Body.CopyToAsync(memoryStream, cancellationToken);
        var imageBytes = memoryStream.ToArray();

        if (imageBytes.Length == 0)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "El archivo está vacío.", HttpStatusCode.BadRequest);
        }

        if (imageBytes.Length > MaxImageBytes)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "La imagen no debe exceder 10 MB.", HttpStatusCode.BadRequest);
        }

        try
        {
            var result = await _extractionAgent.ExtractAsync(imageBytes, contentType, cancellationToken);
            return await FunctionResponseFactory.SuccessResponseAsync(request, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FoodLabelExtract failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo analizar la imagen de la etiqueta.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record SaveFoodLabelRequest(
        string MealType,
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
        string? ConsumedAtIso,
        double? Quantity = null);

    public sealed record SaveFoodLabelResponse(int MealLogId, int FoodItemId);

    /// <summary>POST /api/foods/label/save - the user confirmed the (possibly edited)
    /// extracted data: finds-or-creates the matching global FoodItem row (by normalized
    /// name+brand) and logs it as a meal for the calling person.</summary>
    [Function("FoodLabelSave")]
    public async Task<HttpResponseData> SaveAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/label/save")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, "La base de datos no está configurada.", HttpStatusCode.ServiceUnavailable);
        }

        SaveFoodLabelRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<SaveFoodLabelRequest>(cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo JSON inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Name))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "'name' es obligatorio.", HttpStatusCode.BadRequest);
        }

        var parsedMealType = Enum.TryParse<MealType>(body.MealType, ignoreCase: true, out var mt) ? mt : MealType.Snack;
        var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(body.ConsumedAtIso, DateTime.UtcNow);
        var matchKey = $"{body.Name.Trim().ToLowerInvariant()}|{body.Brand?.Trim().ToLowerInvariant() ?? string.Empty}";
        var quantity = body.Quantity is > 0 ? body.Quantity.Value : 1.0;
        double? Scale(double? value) => value.HasValue ? value.Value * quantity : null;

        try
        {
            var azureObjectIdHeader = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
            _logger.LogInformation("FoodLabelSave: Received request | x-msal-user header={header} | MealType={mealType} | Name={name}", 
                string.IsNullOrWhiteSpace(azureObjectIdHeader) ? "[MISSING]" : azureObjectIdHeader, 
                body.MealType, 
                body.Name);

            var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(request, cancellationToken);
            _logger.LogInformation("FoodLabelSave: PersonId={personId} resolved successfully", personId);

            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            _logger.LogInformation("FoodLabelSave: DbContext created successfully");

            var foodItem = await db.FoodItems.FirstOrDefaultAsync(f => f.MatchKey == matchKey, cancellationToken);
            if (foodItem is null)
            {
                _logger.LogInformation("FoodLabelSave: Creating new FoodItem for matchKey={matchKey}", matchKey);
                foodItem = new FoodItem
                {
                    Name = body.Name.Trim(),
                    Brand = body.Brand,
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
                    IngredientsText = body.IngredientsText,
                    MatchKey = matchKey,
                };
                db.FoodItems.Add(foodItem);
            }
            else
            {
                _logger.LogInformation("FoodLabelSave: Found existing FoodItem id={foodItemId} for matchKey={matchKey}", foodItem.Id, matchKey);
            }

            foodItem.TimesLogged++;

            var quantityLabel = quantity == 1 ? string.Empty : $" (x{quantity:0.##} porciones)";

            var mealLog = new MealLog
            {
                PersonId = personId,
                MealType = parsedMealType,
                Description = (string.IsNullOrWhiteSpace(body.Brand) ? foodItem.Name : $"{foodItem.Name} ({body.Brand})") + quantityLabel,
                ServingSize = body.ServingSize,
                Calories = Scale(body.Calories),
                ProteinGrams = Scale(body.ProteinGrams),
                CarbsGrams = Scale(body.CarbsGrams),
                FatGrams = Scale(body.FatGrams),
                SaturatedFatGrams = Scale(body.SaturatedFatGrams),
                SugarGrams = Scale(body.SugarGrams),
                FiberGrams = Scale(body.FiberGrams),
                SodiumMilligrams = Scale(body.SodiumMilligrams),
                PotassiumMilligrams = Scale(body.PotassiumMilligrams),
                CalciumMilligrams = Scale(body.CalciumMilligrams),
                IronMilligrams = Scale(body.IronMilligrams),
                MagnesiumMilligrams = Scale(body.MagnesiumMilligrams),
                VitaminAMicrograms = Scale(body.VitaminAMicrograms),
                SourceBreakdown = $"{foodItem.Name}{quantityLabel}: {Scale(body.Calories)?.ToString("0") ?? "?"} kcal (etiqueta escaneada)",
                RecordedAtUtc = recordedAt,
                FoodItem = foodItem,
            };
            db.MealLogs.Add(mealLog);
            _logger.LogInformation("FoodLabelSave: MealLog created for PersonId={personId}, MealType={mealType}", personId, parsedMealType);

            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("FoodLabelSave: Database save completed. MealLogId={mealLogId}, FoodItemId={foodItemId}", mealLog.Id, foodItem.Id);

            return await FunctionResponseFactory.SuccessResponseAsync(request, new SaveFoodLabelResponse(mealLog.Id, foodItem.Id));
        }
        catch (DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "FoodLabelSave FAILED - DbUpdateException: {message} | Inner: {inner} | StackTrace: {stackTrace}", 
                dbEx.Message, dbEx.InnerException?.Message ?? "none", dbEx.StackTrace ?? "none");
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, 
                $"Error en base de datos: {dbEx.InnerException?.Message ?? dbEx.Message}", 
                HttpStatusCode.InternalServerError);
        }
        catch (InvalidOperationException ioEx)
        {
            _logger.LogError(ioEx, "FoodLabelSave FAILED - InvalidOperationException: {message} | Inner: {inner} | StackTrace: {stackTrace}", 
                ioEx.Message, ioEx.InnerException?.Message ?? "none", ioEx.StackTrace ?? "none");
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                $"Operación inválida: {ioEx.Message}",
                HttpStatusCode.InternalServerError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FoodLabelSave FAILED - Unexpected Exception ({exceptionType}): {message} | Inner: {inner} | StackTrace: {stackTrace}", 
                ex.GetType().Name, ex.Message, ex.InnerException?.Message ?? "none", ex.StackTrace ?? "none");
            return await FunctionResponseFactory.ErrorResponseAsync(
                request, 
                $"No se pudo guardar el alimento: {ex.GetType().Name}: {ex.Message}", 
                HttpStatusCode.InternalServerError);
        }
    }
}
