using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Common;

/// <summary>
/// Researches real, evidence-based nutrition and exercise plan approaches (Mayo Clinic,
/// OMS/WHO, ACSM, Harvard, etc.) matching a person's profile and stated goals, using Azure
/// AI Foundry's "Grounding with Bing Search" tool for a live web search instead of relying
/// only on the model's built-in knowledge. Returns free-text research notes (NOT JSON) -
/// GoalsAgent feeds these notes as grounding context into its own JSON-structured plan.
///
/// Same Foundry Agent Service pattern as BingFoodSearchProvider (separate PersistentAgentsClient,
/// not the plain Azure OpenAI ChatClient - Bing Grounding is only available through that
/// service, via the existing "Grounding with Bing Search" connection/deployment already
/// configured for BingFoodSearchProvider).
/// </summary>
public sealed class BingPlanSearchProvider
{
    private const string AgentInstructions = """
        Eres un investigador experto en planes de nutrición y ejercicio basados en evidencia.
        Dado el perfil de una persona (peso, estatura, IMC, nivel de actividad) y sus
        objetivos, usa la herramienta de búsqueda de Bing para encontrar enfoques reales y
        confiables (de fuentes como Mayo Clinic, OMS/WHO, ACSM, Harvard Health, etc.)
        adecuados para ese perfil y objetivo.

        Responde con un resumen breve en texto plano (NO uses JSON ni markdown), de 5 a 8
        líneas, con las recomendaciones más relevantes y basadas en evidencia que
        encontraste (déficit/superávit calórico razonable, tipo de ejercicio recomendado,
        ritmo de cambio de peso saludable, etc.), mencionando la fuente si es posible.
        """;

    private readonly PersistentAgentsClient? _client;
    private readonly string? _modelDeploymentName;
    private readonly string? _bingConnectionId;
    private readonly SemaphoreSlim _agentInitLock = new(1, 1);
    private PersistentAgent? _agent;

    public BingPlanSearchProvider(IConfiguration configuration)
    {
        var projectEndpoint = configuration["BingProjectEndpoint"];
        _bingConnectionId = configuration["BingConnectionId"];
        _modelDeploymentName = configuration["BingGroundingModelDeploymentName"];

        if (string.IsNullOrWhiteSpace(projectEndpoint)
            || string.IsNullOrWhiteSpace(_bingConnectionId)
            || string.IsNullOrWhiteSpace(_modelDeploymentName))
        {
            _client = null;
            return;
        }

        _client = new PersistentAgentsClient(projectEndpoint, new DefaultAzureCredential());
    }

    public bool IsConfigured => _client is not null;

    /// <summary>
    /// Runs a Bing-grounded research pass for the given profile/goals description and
    /// returns the plain-text research notes produced by the model, or null if this
    /// provider isn't configured or the search failed.
    /// </summary>
    public async Task<string?> SearchNutritionExercisePlanNotesAsync(string profileAndGoalsDescription, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            return null;
        }

        var agent = await GetOrCreateAgentAsync(cancellationToken);

        PersistentAgentThread thread = await _client.Threads.CreateThreadAsync(cancellationToken: cancellationToken);
        try
        {
            await _client.Messages.CreateMessageAsync(
                thread.Id, MessageRole.User, profileAndGoalsDescription, cancellationToken: cancellationToken);

            ThreadRun run = await _client.Runs.CreateRunAsync(thread.Id, agent.Id, cancellationToken: cancellationToken);

            while (run.Status == RunStatus.Queued || run.Status == RunStatus.InProgress)
            {
                await Task.Delay(500, cancellationToken);
                run = await _client.Runs.GetRunAsync(thread.Id, run.Id, cancellationToken);
            }

            if (run.Status != RunStatus.Completed)
            {
                return null;
            }

            await foreach (var message in _client.Messages.GetMessagesAsync(
                thread.Id, order: ListSortOrder.Descending, cancellationToken: cancellationToken))
            {
                if (message.Role != MessageRole.Agent)
                {
                    continue;
                }

                foreach (var content in message.ContentItems)
                {
                    if (content is MessageTextContent textContent)
                    {
                        return textContent.Text;
                    }
                }
            }

            return null;
        }
        finally
        {
            await _client.Threads.DeleteThreadAsync(thread.Id, cancellationToken);
        }
    }

    private async Task<PersistentAgent> GetOrCreateAgentAsync(CancellationToken cancellationToken)
    {
        if (_agent is not null)
        {
            return _agent;
        }

        await _agentInitLock.WaitAsync(cancellationToken);
        try
        {
            if (_agent is not null)
            {
                return _agent;
            }

            var bingTool = new BingGroundingToolDefinition(
                new BingGroundingSearchToolParameters([new BingGroundingSearchConfiguration(_bingConnectionId)]));

            _agent = await _client!.Administration.CreateAgentAsync(
                model: _modelDeploymentName,
                name: "PersonalAgent-GoalsPlanBingSearch",
                instructions: AgentInstructions,
                tools: [bingTool],
                cancellationToken: cancellationToken);

            return _agent;
        }
        finally
        {
            _agentInitLock.Release();
        }
    }
}
