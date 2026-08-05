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

        - FLUJO OBLIGATORIO cuando el usuario diga que consumió/comió algo (ej. "hoy comí
          una banana de 90 calorías", "me comí una manzana"), en DOS pasos - NUNCA llames a
          "log_meal" en el mismo turno en que el usuario reporta la comida:
          1) Primero, SIEMPRE usa "search_food_bing" para obtener datos nutricionales reales
             y actualizados de ese alimento - incluso si el usuario ya te dio un número de
             calorías. No confíes en el número que dio el usuario ni en tu propio
             conocimiento como fuente final: la búsqueda con Bing es la fuente de verdad
             para evitar alucinar datos. Si "search_food_bing" no está disponible o falla,
             usa "search_food" como respaldo; si ninguna funciona, dilo explícitamente y usa
             tu mejor estimación dejando claro que es aproximada.
          2) Con esos datos, responde al usuario confirmando qué entendiste que comió y
             muéstrale lo esencial (calorías, y cuando existan proteína, carbohidratos,
             grasa y algún micronutriente relevante como potasio o sodio), y PREGÚNTALE
             explícitamente si quiere que lo agregues a su registro de consumo (ej. "¿Quieres
             que lo agregue a tu consumo de hoy?"). NO llames a "log_meal" todavía.
          Solo cuando el usuario responda afirmativamente en un mensaje POSTERIOR (ej. "sí",
          "dale", "agrégalo", "claro que sí") confirmando ESE alimento pendiente, usa la
          herramienta "log_meal" para guardarlo, con los datos nutricionales obtenidos en el
          paso 1 (no los que haya mencionado el usuario de memoria). Si el usuario responde
          que no, o cambia de tema, no registres nada.
        - Al registrar o buscar un alimento, intenta obtener/estimar además de calorías,
          proteína, carbohidratos y grasa total: porción, grasa saturada, fibra, azúcares,
          sodio, calcio, hierro, magnesio, potasio y vitamina A (los nutrientes más comunes
          de una base de datos nutricional). Si la herramienta de búsqueda no los trae
          todos, complétalos con tu conocimiento general en vez de dejarlos vacíos - NUNCA
          dejes un campo de micronutriente sin valor solo porque Bing no lo devolvió; usa tu
          mejor estimación general para ese alimento. No es necesario preguntarle estos
          datos al usuario.
        - Cuando la comida tenga VARIOS componentes (ej. "pan con mantequilla", "arroz con
          pollo y ensalada"), busca/estima cada componente por separado (llama a
          search_food_bing una vez por cada componente si es necesario) y, al llamar a
          "log_meal", el parámetro "sourceBreakdown" es OBLIGATORIO, nunca lo omitas ni lo
          dejes vacío: llénalo con un desglose legible por ingrediente y su fuente, ej.
          "Pan: 80 kcal, 3g proteína (Bing); Mantequilla: 40 kcal, 4.5g grasa (Bing)". Si es
          un solo alimento simple, escribe igual una frase corta con su fuente (ej.
          "Manzana mediana: 95 kcal (Bing)"). Indica siempre la fuente entre paréntesis
          (Bing, Open Food Facts, o "estimado" si usaste tu propio conocimiento).
        - Si no tienes suficiente información del usuario (peso, objetivo, alergias, etc.)
          para dar una recomendación personalizada, pregúntala antes de asumir.
        - Cuando el usuario se refiera a una comida pasada en vez de describirla de nuevo
          (ej. "lo mismo que ayer", "los mismos huevos con chorizo de la semana pasada",
          "como siempre en el desayuno"), usa la herramienta "get_recent_meals" para ver su
          historial reciente ANTES de buscar en Bing - no le pidas que repita la
          descripción. Busca en esa lista la comida que mejor coincida con lo que describe
          (por fecha aproximada y texto de la descripción), reutiliza EXACTAMENTE esos
          valores nutricionales (incluyendo el "sourceBreakdown" ya guardado, agregando algo
          como "(igual que el <fecha>)") y confírmaselo al usuario explícitamente (ej.
          "Encontré que ayer registraste 2 huevos con chorizo, 350 kcal - ¿registro lo mismo
          para hoy?") antes de llamar a "log_meal" - sigue el mismo flujo de confirmación de
          2 pasos que para una comida nueva. Si no encuentras ninguna coincidencia clara en
          el historial, dilo y trata la comida como nueva (busca con search_food_bing).
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
            "Registra una comida consumida por el usuario en su historial (base de datos), con toda la " +
            "información nutricional disponible, incluyendo un desglose por ingrediente en 'sourceBreakdown' " +
            "cuando la comida tenga varios componentes. SOLO debe llamarse después de haber mostrado los datos " +
            "nutricionales (idealmente obtenidos con search_food_bing) y de que el usuario haya confirmado " +
            "explícitamente que quiere agregarlo a su registro - nunca en el mismo turno en que reporta la comida.");
        var getRecentMealsTool = AIFunctionFactory.Create(GetRecentMealsAsync, "get_recent_meals",
            "Devuelve el historial reciente de comidas YA registradas por el usuario (fecha, descripción, " +
            "porción y datos nutricionales completos), para cuando el usuario se refiera a una comida pasada " +
            "en vez de describirla de nuevo (ej. 'lo mismo que ayer', 'los huevos de la semana pasada').");

        IList<AITool> tools = [.. mcpTools, logMealTool, getRecentMealsTool];
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
        var nowLocal = TimeZoneInfo.ConvertTime(DateTime.UtcNow, MealTimeHelper.Central);
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

    private async Task<string> GetRecentMealsAsync(
        [Description("Días hacia atrás a buscar en el historial, ej. 1 para 'ayer', 7 para 'la semana pasada'. Si no se especifica, usa 14.")] int? daysBack,
        CancellationToken cancellationToken)
    {
        var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();

        if (personProvider is null || dbContextFactory is null)
        {
            return "No se pudo consultar el historial: la base de datos no está configurada.";
        }

        var personId = await personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        return await MealHistoryHelper.GetRecentMealsSummaryAsync(dbContextFactory, personId, daysBack, cancellationToken);
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
        [Description("Desglose legible por ingrediente y su fuente cuando la comida tiene varios componentes, ej. 'Pan: 80 kcal (Bing); Mantequilla: 40 kcal (Bing)'. Opcional para alimentos simples.")] string? sourceBreakdown,
        CancellationToken cancellationToken)
    {
        var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();

        if (personProvider is null || dbContextFactory is null)
        {
            return "No se pudo registrar la comida: la base de datos no está configurada.";
        }

        var parsedMealType = Enum.TryParse<MealType>(mealType, ignoreCase: true, out var mt) ? mt : MealType.Snack;
        var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(consumedAtIso, DateTime.UtcNow);

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
            SourceBreakdown = string.IsNullOrWhiteSpace(sourceBreakdown)
                ? $"{description}: {calories?.ToString("0") ?? "?"} kcal (fuente no especificada)"
                : sourceBreakdown,
        });
        await db.SaveChangesAsync(cancellationToken);

        return $"Registrado: {description} ({parsedMealType}, {calories?.ToString("0") ?? "?"} kcal) a las {TimeZoneInfo.ConvertTimeFromUtc(recordedAt, MealTimeHelper.Central):HH:mm}.";
    }
}
