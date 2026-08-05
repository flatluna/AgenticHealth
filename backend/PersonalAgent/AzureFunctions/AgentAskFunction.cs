using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PersonalAgent.Agents;

namespace PersonalAgent.AzureFunctions;

public sealed class AgentAskFunction
{
    private readonly OrchestratorAgent _orchestrator;
    private readonly ILogger<AgentAskFunction> _logger;

    public AgentAskFunction(OrchestratorAgent orchestrator, ILogger<AgentAskFunction> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public sealed record AskRequest(string Message, string? SessionId, string? UserName);

    public sealed record AskResponse(string Reply, string SessionId);

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
            var reply = await _orchestrator.AskAsync(body.Message, sessionId, body.UserName, cancellationToken);
            return await FunctionResponseFactory.SuccessResponseAsync(request, new AskResponse(reply, sessionId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentAsk failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "Ocurrió un error al procesar la petición.", HttpStatusCode.InternalServerError);
        }
    }
}
