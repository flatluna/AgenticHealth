using System.Linq;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PersonalAgent.Agents;
using PersonalAgent.Common;

namespace PersonalAgent.AzureFunctions;

public sealed class AgentAskFunction
{
    private readonly OrchestratorAgent _orchestrator;
    private readonly AgentProgressTracker _progressTracker;
    private readonly PendingMealTracker _pendingMealTracker;
    private readonly FoodSourceChoiceTracker _foodSourceChoiceTracker;
    private readonly ILogger<AgentAskFunction> _logger;

    public AgentAskFunction(
        OrchestratorAgent orchestrator,
        AgentProgressTracker progressTracker,
        PendingMealTracker pendingMealTracker,
        FoodSourceChoiceTracker foodSourceChoiceTracker,
        ILogger<AgentAskFunction> logger)
    {
        _orchestrator = orchestrator;
        _progressTracker = progressTracker;
        _pendingMealTracker = pendingMealTracker;
        _foodSourceChoiceTracker = foodSourceChoiceTracker;
        _logger = logger;
    }

    public sealed record AskRequest(string Message, string? SessionId, string? UserName);

    public sealed record AskResponse(string Reply, string SessionId, PendingMealDto? PendingMeal, FoodSourceChoiceDto? FoodSourceChoice);

    [Function("AgentAsk")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "agent/ask")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!_orchestrator.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "El agente no está configurado (faltan credenciales de Azure OpenAI en local.settings.json).",
                HttpStatusCode.ServiceUnavailable);
        }

        AskRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<AskRequest>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid AgentAsk request body");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Cuerpo de la petición inválido.", HttpStatusCode.BadRequest);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.Message))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "El campo 'message' es obligatorio.", HttpStatusCode.BadRequest);
        }

        // The caller (frontend) generates and persists this id across turns so the agent can
        // maintain conversation memory (AgentSession) between requests; if omitted, a new
        // conversation/session is started.
        var sessionId = string.IsNullOrWhiteSpace(body.SessionId) ? Guid.NewGuid().ToString("N") : body.SessionId;

        try
        {
            var azureObjectId = request.Headers.TryGetValues("x-msal-user", out var values) ? values.FirstOrDefault() : null;
            var reply = await _orchestrator.AskAsync(body.Message, sessionId, azureObjectId, body.UserName, cancellationToken);
            // Fix literal \n escape sequences that the LLM model sometimes generates as two-char literals instead of actual newlines
            var cleanReply = reply.Replace("\\n", "\n");
            var pendingMeal = _pendingMealTracker.Take(sessionId);
            var foodSourceChoice = _foodSourceChoiceTracker.Take(sessionId);
            return await FunctionResponseFactory.SuccessResponseAsync(request, new AskResponse(cleanReply, sessionId, pendingMeal, foodSourceChoice));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentAsk failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al procesar la petición.", HttpStatusCode.InternalServerError);
        }
    }

    public sealed record AgentProgressResponse(IReadOnlyList<string> Messages);

    /// <summary>GET /api/agent/progress?sessionId=X - drains any short status lines
    /// published so far for this session (e.g. one per ingredient as DietAgent's parallel
    /// Bing searches resolve), so the frontend can poll this while POST /api/agent/ask is
    /// still in flight and show them as a "still working..." trail.</summary>
    [Function("AgentProgress")]
    public async Task<HttpResponseData> GetProgressAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "agent/progress")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        var query = System.Web.HttpUtility.ParseQueryString(request.Url.Query);
        var sessionId = query["sessionId"];
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Falta el parámetro 'sessionId'.", HttpStatusCode.BadRequest);
        }

        var messages = _progressTracker.Drain(sessionId);
        return await FunctionResponseFactory.SuccessResponseAsync(request, new AgentProgressResponse(messages));
    }
}
