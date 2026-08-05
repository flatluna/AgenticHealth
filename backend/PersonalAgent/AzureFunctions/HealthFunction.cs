using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PersonalAgent.Agents;

namespace PersonalAgent.AzureFunctions;

public sealed class HealthFunction
{
    private readonly OrchestratorAgent _orchestrator;

    public HealthFunction(OrchestratorAgent orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [Function("Health")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        return await FunctionResponseFactory.SuccessResponseAsync(request, new
        {
            status = "ok",
            agentConfigured = _orchestrator.IsConfigured,
        });
    }
}
