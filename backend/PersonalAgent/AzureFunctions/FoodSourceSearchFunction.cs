using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PersonalAgent.Agents;
using PersonalAgent.Common;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Backs the 3-button chooser DietAgent's fast path offers ("Catálogo local"/"Edamam"/
/// "Internet") after extracting food(s) from a chat message - see FoodSourceChoiceTracker/
/// AgentAskFunction.AskResponse.FoodSourceChoice for how the choice reaches the frontend.
/// The user clicks one button, the frontend POSTs back here with the SAME
/// FoodSourceChoiceDto plus which source they picked, and this runs ONLY that specialized
/// search (no auto-fallback to another source - the user can just click a different button).
/// </summary>
public sealed class FoodSourceSearchFunction
{
    private readonly DietAgent _dietAgent;
    private readonly ILogger<FoodSourceSearchFunction> _logger;

    public FoodSourceSearchFunction(DietAgent dietAgent, ILogger<FoodSourceSearchFunction> logger)
    {
        _dietAgent = dietAgent;
        _logger = logger;
    }

    [Function("FoodSourceSearchOptions")]
    public HttpResponseData OptionsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/search-source")]
        HttpRequestData request)
    {
        return FunctionResponseFactory.PreflightResponseAsync(request);
    }

    [Function("FoodSourceSearchDirectOptions")]
    public HttpResponseData OptionsDirectAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "foods/search-source-direct")]
        HttpRequestData request)
    {
        return FunctionResponseFactory.PreflightResponseAsync(request);
    }

    public sealed record SearchSourceRequest(FoodSourceChoiceDto Choice, string Source);

    public sealed record SearchSourceResponse(string Reply, PendingMealDto? PendingMeal);

    public sealed record SearchSourceDirectRequest(string Message, string Source);

    [Function("FoodSourceSearch")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/search-source")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!_dietAgent.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "El agente no está configurado (faltan credenciales de Azure OpenAI en local.settings.json).",
                HttpStatusCode.ServiceUnavailable);
        }

        SearchSourceRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<SearchSourceRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid FoodSourceSearch request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || body.Choice is null || string.IsNullOrWhiteSpace(body.Source))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Los campos 'choice' y 'source' son obligatorios.", HttpStatusCode.BadRequest);
        }

        try
        {
            var azureObjectId = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
            var (reply, pendingMeal) = await _dietAgent.SearchBySpecificSourceAsync(body.Choice, body.Source, azureObjectId, cancellationToken);
            var cleanReply = reply.Replace("\\n", "\n");
            return await FunctionResponseFactory.SuccessResponseAsync(request, new SearchSourceResponse(cleanReply, pendingMeal));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FoodSourceSearch failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al procesar la búsqueda.", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>Backs the 3 permanent "Catálogo"/"Edamam"/"Internet" buttons shown next to the
    /// chat input - the user types a message and taps one of these INSTEAD OF "Enviar", so the
    /// source is chosen in the same action as sending, skipping the "¿de dónde busco?" round
    /// trip entirely.</summary>
    [Function("FoodSourceSearchDirect")]
    public async Task<HttpResponseData> RunDirectAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "foods/search-source-direct")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!_dietAgent.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "El agente no está configurado (faltan credenciales de Azure OpenAI en local.settings.json).",
                HttpStatusCode.ServiceUnavailable);
        }

        SearchSourceDirectRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<SearchSourceDirectRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid FoodSourceSearchDirect request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Message) || string.IsNullOrWhiteSpace(body.Source))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Los campos 'message' y 'source' son obligatorios.", HttpStatusCode.BadRequest);
        }

        try
        {
            var azureObjectId = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
            var (reply, pendingMeal) = await _dietAgent.SearchByPromptAndSourceAsync(body.Message, body.Source, azureObjectId, cancellationToken);
            var cleanReply = reply.Replace("\\n", "\n");
            return await FunctionResponseFactory.SuccessResponseAsync(request, new SearchSourceResponse(cleanReply, pendingMeal));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FoodSourceSearchDirect failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al procesar la búsqueda.", HttpStatusCode.InternalServerError);
        }
    }
}
