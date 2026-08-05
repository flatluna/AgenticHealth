using System.ClientModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OpenAI.Chat;
using PersonalAgent.Common;
using PersonalAgent.Data;
using PersonalAgent.Skills;

namespace PersonalAgent.Agents;

/// <summary>
/// Specialized agent for diet, nutrition and calorie-counting questions.
/// Follows the same self-configuring pattern as HumanOS agents: reads Azure OpenAI
/// settings from IConfiguration, falls back to DefaultAzureCredential when no API key
/// is set, and exposes IsConfigured so callers can fail gracefully instead of crashing.
///
/// Unlike the other agents, DietAgent also connects to the local Food MCP server (see
/// mcp/FoodMcpServer) to look up real nutrition/calorie data instead of guessing, and can
/// log meals to PersonalAgentDB via the "log_meal" tool. It also keeps a per-conversation
/// AgentSession (keyed by the caller-supplied sessionId) so it remembers earlier turns -
/// e.g. "una banana tiene 120 kcal" said in turn 1 can be referenced in turn 2 ("agrégala a
/// mi desayuno de hoy") without the user repeating the number.
/// </summary>
public sealed class DietAgent
{
    private const string Instructions = """
        Eres DietAgent, un asistente experto en nutrición, dietas y conteo de calorías.

        Reglas:
        - Responde siempre en español, de forma clara y concisa.
        - Cuando el usuario pregunte por el valor nutricional o calórico de un alimento,
          usa PRIMERO la herramienta "search_food_bing" (busca en la web en tiempo real con
          Bing y devuelve un JSON con calorías, macros y los micronutrientes más comunes).
          Si esa herramienta no está disponible o falla, usa "search_food" (base de datos
          Open Food Facts) como alternativa. No inventes datos si tienes una herramienta
          disponible para buscarlos.
        - Cuando el usuario te diga que consumió/comió algo y te pida registrarlo o sumarlo
          a su consumo (ej. "me comí una banana a las 8:30am, súmala a mi desayuno de hoy"),
          usa la herramienta "log_meal" para guardarlo. Usa los datos nutricionales que ya
          conoces de la conversación (por ejemplo, si ya buscaste o mencionaste las calorías
          de ese alimento antes) en vez de volver a preguntarlos, a menos que falten datos
          esenciales como las calorías.
        - Al registrar o buscar un alimento, intenta obtener/estimar además de calorías,
          proteína, carbohidratos y grasa total: porción, grasa saturada, fibra, azúcares,
          sodio, calcio, hierro, magnesio, potasio y vitamina A (los nutrientes más comunes
          de una base de datos nutricional). Si la herramienta de búsqueda no los trae
          todos, complétalos con tu conocimiento general en vez de dejarlos vacíos. No es
          necesario preguntarle estos datos al usuario.
        - Si no tienes suficiente información del usuario (peso, objetivo, alergias, etc.)
          para dar una recomendación personalizada, pregúntala antes de asumir.
        - No eres un médico: para condiciones médicas serias, recomienda consultar a un
          profesional de la salud.
        - Cada mensaje del usuario incluye la fecha y hora actual real entre corchetes
          (ej. "[Fecha y hora actual: 2026-08-03 14:00 (lunes)]"). Úsala como referencia de
          "hoy" al registrar comidas (log_meal) cuando el usuario diga expresiones relativas
          como "hoy", "ayer" o solo una hora sin fecha (ej. "a las 8:30am"). No asumas otro día.
        """;

    private readonly ChatClient? _chatClient;
    private readonly FoodMcpClientProvider _mcpClientProvider;
    private readonly BingFoodSearchProvider _bingFoodSearchProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly SemaphoreSlim _mcpToolsInitLock = new(1, 1);
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private IList<AITool>? _mcpTools;

    public DietAgent(
        IConfiguration configuration,
        FoodMcpClientProvider mcpClientProvider,
        BingFoodSearchProvider bingFoodSearchProvider,
        IServiceProvider serviceProvider)
    {
        _mcpClientProvider = mcpClientProvider;
        _bingFoodSearchProvider = bingFoodSearchProvider;
        _serviceProvider = serviceProvider;

        var endpoint = configuration["AzureOpenAIEndpoint"];
        var deploymentName = configuration["AzureOpenAIDeploymentName"];
        var apiKey = configuration["AzureOpenAIApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deploymentName))
        {
            _chatClient = null;
            return;
        }

        AzureOpenAIClient azureClient = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));

        _chatClient = azureClient.GetChatClient(deploymentName);
    }

    public bool IsConfigured => _chatClient is not null;

    public async Task<string> AskAsync(string prompt, string sessionId, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("DietAgent is not configured (missing Azure OpenAI settings).");
        }

        var mcpTools = await GetOrCreateMcpToolsAsync(cancellationToken);
        var logMealTool = AIFunctionFactory.Create(LogMealAsync, "log_meal",
            "Registra una comida consumida por el usuario en su historial (base de datos), con toda la información nutricional disponible.");

        IList<AITool> tools = [.. mcpTools, logMealTool];
        if (_bingFoodSearchProvider.IsConfigured)
        {
            var bingFoodTool = AIFunctionFactory.Create(SearchFoodBingAsync, "search_food_bing",
                "Busca en la web (Bing) la información nutricional completa de un alimento: calorías, macros " +
                "(proteína, carbohidratos, grasa total, grasa saturada, azúcares, fibra) y micronutrientes " +
                "comunes (sodio, potasio, calcio, hierro, magnesio, vitamina A). Devuelve un JSON.");
            tools = [.. tools, bingFoodTool];
        }
        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: Instructions, name: "DietAgent", tools: tools);
        var session = await GetOrCreateSessionAsync(agent, sessionId, cancellationToken);

        var skill = DietSkillSelector.Select(prompt);
        var skillGuidance = DietSkillLibrary.InstructionsFor(skill);
        var nowLocal = DateTime.Now;
        var fullPrompt = $"[Fecha y hora actual: {nowLocal:yyyy-MM-dd HH:mm} ({nowLocal:dddd})]\n" +
            $"[Guía de skill: {skillGuidance}]\n\nPregunta del usuario: {prompt}";

        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);
        return response.Text;
    }

    private async Task<string> SearchFoodBingAsync(
        [Description("Nombre o descripción del alimento a buscar, ej. 'una banana mediana' o 'pechuga de pollo 100g'.")] string foodDescription,
        CancellationToken cancellationToken)
    {
        var json = await _bingFoodSearchProvider.SearchFoodNutritionJsonAsync(foodDescription, cancellationToken);
        return json ?? "No se encontraron resultados en Bing para ese alimento.";
    }

    private async Task<IList<AITool>> GetOrCreateMcpToolsAsync(CancellationToken cancellationToken)
    {
        if (_mcpTools is not null)
        {
            return _mcpTools;
        }

        await _mcpToolsInitLock.WaitAsync(cancellationToken);
        try
        {
            _mcpTools ??= await _mcpClientProvider.GetToolsAsync(cancellationToken);
            return _mcpTools;
        }
        finally
        {
            _mcpToolsInitLock.Release();
        }
    }

    private async Task<AgentSession> GetOrCreateSessionAsync(AIAgent agent, string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            return existing;
        }

        var session = await agent.CreateSessionAsync(cancellationToken);
        return _sessions.GetOrAdd(sessionId, session);
    }

    private async Task<string> LogMealAsync(
        [Description("Tipo de comida: breakfast, lunch, dinner o snack.")] string mealType,
        [Description("Descripción breve de lo consumido, ej. 'una banana'.")] string description,
        [Description("Tamaño de porción, ej. '100 g' o '1 unidad mediana'.")] string? servingSize,
        [Description("Calorías totales (kcal).")] double? calories,
        [Description("Proteína en gramos.")] double? proteinGrams,
        [Description("Carbohidratos en gramos.")] double? carbsGrams,
        [Description("Grasas totales en gramos.")] double? fatGrams,
        [Description("Grasas saturadas en gramos.")] double? saturatedFatGrams,
        [Description("Azúcares en gramos.")] double? sugarGrams,
        [Description("Fibra en gramos.")] double? fiberGrams,
        [Description("Sodio en miligramos.")] double? sodiumMilligrams,
        [Description("Potasio en miligramos.")] double? potassiumMilligrams,
        [Description("Calcio en miligramos.")] double? calciumMilligrams,
        [Description("Hierro en miligramos.")] double? ironMilligrams,
        [Description("Magnesio en miligramos.")] double? magnesiumMilligrams,
        [Description("Vitamina A en microgramos.")] double? vitaminAMicrograms,
        [Description("Hora en que se consumió, formato ISO 8601 (ej. '2026-08-03T08:30:00'). Si no se especifica, se usa la hora actual.")] string? consumedAtIso,
        CancellationToken cancellationToken)
    {
        var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();

        if (personProvider is null || dbContextFactory is null)
        {
            return "No se pudo registrar la comida: la base de datos no está configurada.";
        }

        var parsedMealType = Enum.TryParse<MealType>(mealType, ignoreCase: true, out var mt) ? mt : MealType.Snack;
        var recordedAt = DateTime.TryParse(consumedAtIso, out var parsed) ? parsed.ToUniversalTime() : DateTime.UtcNow;

        var personId = await personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.MealLogs.Add(new MealLog
        {
            PersonId = personId,
            MealType = parsedMealType,
            Description = description,
            ServingSize = servingSize,
            Calories = calories,
            ProteinGrams = proteinGrams,
            CarbsGrams = carbsGrams,
            FatGrams = fatGrams,
            SaturatedFatGrams = saturatedFatGrams,
            SugarGrams = sugarGrams,
            FiberGrams = fiberGrams,
            SodiumMilligrams = sodiumMilligrams,
            PotassiumMilligrams = potassiumMilligrams,
            CalciumMilligrams = calciumMilligrams,
            IronMilligrams = ironMilligrams,
            MagnesiumMilligrams = magnesiumMilligrams,
            VitaminAMicrograms = vitaminAMicrograms,
            RecordedAtUtc = recordedAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        return $"Registrado: {description} ({parsedMealType}, {calories?.ToString("0") ?? "?"} kcal) a las {recordedAt:HH:mm}.";
    }
}
