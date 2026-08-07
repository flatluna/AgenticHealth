using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using System.Linq;

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
        Eres un asistente experto en nutrición. Cuando te pregunten por uno o varios
        alimentos, usa la herramienta de búsqueda de Bing para encontrar su información
        nutricional real y actualizada (por ejemplo de bases de datos nutricionales,
        fabricantes o fuentes confiables), en vez de inventar los valores. Si el alimento es
        de una marca o cadena específica (ej. "Big Mac de McDonald's", "Whopper de Burger
        King"), prioriza el sitio oficial de esa marca/cadena sobre fuentes genéricas. Si te
        piden varios alimentos en el mismo mensaje, busca cada uno por separado (puedes y
        debes hacer varias búsquedas de Bing en esa misma respuesta) para no mezclar datos
        de un alimento con otro.

        Cada objeto de resultado tiene esta forma exacta, usando null si un dato no se
        encuentra:
        {
          "query": string,
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
          "vitaminAMicrograms": number|null,
          "source": string|null,
          "sourceUrl": string|null
        }
        "query" debe repetir EXACTAMENTE (verbatim) el texto del alimento tal como te lo
        pidieron, para poder emparejar cada resultado con su pregunta. Todos los valores
        numéricos son por la porción indicada en "servingSize". "source" es OBLIGATORIO
        cuando encuentres datos: el nombre corto y legible del sitio u organización de donde
        salió la información (ej. "Sitio oficial de McDonald's", "USDA FoodData Central",
        "MyFitnessPal"), NUNCA solo "Bing" o "internet" - Bing es el buscador, no la fuente.
        "sourceUrl" es la URL exacta de la página consultada, o null si no puedes
        determinarla con certeza. Si de verdad no encontraste ningún resultado para un
        alimento, deja "source" y "sourceUrl" en null junto con los demás campos (pero
        conserva su "query").

        Si te preguntan por UN SOLO alimento, responde ÚNICAMENTE con ESE objeto (sin
        arreglo envolvente, sin texto adicional, sin markdown, sin ```json). Si te preguntan
        por VARIOS alimentos a la vez (una lista numerada), responde ÚNICAMENTE con un
        arreglo JSON de esos mismos objetos, en el mismo orden en que te los pidieron, uno
        por alimento - sin texto adicional, sin markdown.
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
    /// object text produced by the model (per <see cref="AgentInstructions"/>), or null if
    /// this provider isn't configured or the search failed.
    /// </summary>
    public Task<string?> SearchFoodNutritionJsonAsync(string foodDescription, CancellationToken cancellationToken) =>
        RunNutritionQueryAsync($"Información nutricional de: {foodDescription}", cancellationToken);

    /// <summary>
    /// Same as <see cref="SearchFoodNutritionJsonAsync"/> but looks up MULTIPLE foods in a
    /// single Bing agent thread/run instead of one per food - halves the thread/run overhead
    /// (each is a multi-second round trip on its own) for meals with several ingredients.
    /// Returns the raw JSON ARRAY text produced by the model (one element per food, in the
    /// same order as <paramref name="foodDescriptions"/>, per <see cref="AgentInstructions"/>),
    /// or null if this provider isn't configured or the search failed.
    /// </summary>
    public Task<string?> SearchFoodsNutritionJsonAsync(IReadOnlyList<string> foodDescriptions, CancellationToken cancellationToken)
    {
        var numberedList = string.Join("\n", foodDescriptions.Select((food, index) => $"{index + 1}. {food}"));
        return RunNutritionQueryAsync(
            $"Información nutricional de los siguientes {foodDescriptions.Count} alimentos (responde con un " +
            $"arreglo JSON, uno por cada uno, en el mismo orden):\n{numberedList}",
            cancellationToken);
    }

    private async Task<string?> RunNutritionQueryAsync(string userMessage, CancellationToken cancellationToken)
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
                thread.Id, MessageRole.User, userMessage, cancellationToken: cancellationToken);

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
            // Fire-and-forget: deleting the thread is pure cleanup and doesn't need to block the
            // response - shaves a full round trip off every search. CancellationToken.None so it
            // still runs even if the caller's request has already finished/disconnected.
            var threadId = thread.Id;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _client.Threads.DeleteThreadAsync(threadId, CancellationToken.None);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            });
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
