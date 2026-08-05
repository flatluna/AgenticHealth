using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Common;

/// <summary>
/// Looks up full nutrition-fact details (macro + common micronutrients) for a food item
/// using Azure AI Foundry's "Grounding with Bing Search" tool, which performs a live web
/// search instead of relying only on the model's built-in knowledge.
///
/// Uses the Foundry Agent Service (Azure.AI.Agents.Persistent), NOT the plain Azure OpenAI
/// ChatClient used by the other agents - Bing Grounding is only available through that
/// service, via a "connection" to a Grounding with Bing Search resource that must already
/// exist on the Foundry project (see BingConnectionId below).
///
/// IMPORTANT: the Grounding with Bing Search tool does not work with gpt-5 family models
/// (as of the Foundry docs), so this uses a separate, Bing-compatible deployment
/// (BingGroundingModelDeploymentName, e.g. "gpt4mini" / gpt-4.1-mini) rather than the
/// gpt-5-chat deployment used elsewhere.
/// </summary>
public sealed class BingFoodSearchProvider
{
    private const string AgentInstructions = """
        Eres un asistente experto en nutrición. Cuando te pregunten por un alimento, usa la
        herramienta de búsqueda de Bing para encontrar su información nutricional real y
        actualizada (por ejemplo de bases de datos nutricionales, fabricantes o fuentes
        confiables), en vez de inventar los valores.

        Responde ÚNICAMENTE con un objeto JSON (sin texto adicional, sin markdown, sin
        ```json) con esta forma exacta, usando null si un dato no se encuentra:
        {
          "servingSize": string|null,
          "calories": number|null,
          "proteinGrams": number|null,
          "carbsGrams": number|null,
          "fatGrams": number|null,
          "saturatedFatGrams": number|null,
          "sugarGrams": number|null,
          "fiberGrams": number|null,
          "sodiumMilligrams": number|null,
          "potassiumMilligrams": number|null,
          "calciumMilligrams": number|null,
          "ironMilligrams": number|null,
          "magnesiumMilligrams": number|null,
          "vitaminAMicrograms": number|null
        }
        Todos los valores numéricos son por la porción indicada en "servingSize".
        """;

    private readonly PersistentAgentsClient? _client;
    private readonly string? _modelDeploymentName;
    private readonly string? _bingConnectionId;
    private readonly SemaphoreSlim _agentInitLock = new(1, 1);
    private PersistentAgent? _agent;

    public BingFoodSearchProvider(IConfiguration configuration)
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
    /// Runs a Bing-grounded search for the given food description and returns the raw JSON
    /// text produced by the model (per <see cref="AgentInstructions"/>), or null if this
    /// provider isn't configured or the search failed.
    /// </summary>
    public async Task<string?> SearchFoodNutritionJsonAsync(string foodDescription, CancellationToken cancellationToken)
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
                thread.Id, MessageRole.User, $"Información nutricional de: {foodDescription}", cancellationToken: cancellationToken);

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
                name: "PersonalAgent-NutritionBingSearch",
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
