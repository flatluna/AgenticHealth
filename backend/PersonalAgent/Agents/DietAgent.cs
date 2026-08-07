using System.ClientModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
          usa SIEMPRE PRIMERO la herramienta "search_food_catalog" (busca en nuestro propio
          catálogo de productos, creado por otros usuarios al escanear etiquetas - datos
          reales y ya verificados de la etiqueta, no una estimación). Si "search_food_catalog"
          devuelve al menos una coincidencia, esa es la fuente de verdad: úsala directamente
          para responder, NO llames a ninguna otra herramienta de búsqueda después, y dile al
          usuario que el dato viene de nuestro catálogo de productos (ej. "Encontré Coca-Cola
          355ml en nuestro catálogo de productos: 140 kcal."). Solo cuando "search_food_catalog"
          responda que no encontró ningún producto, busca en este orden: (1) si
          "search_foods_edamam" está disponible, úsala primero (API estructurada de nutrición,
          rápida - responde en segundos); (2) si no está disponible, falla, o no reconoce el
          alimento, usa "search_foods_bing" (busca en la web en tiempo real con Bing, más lenta
          pero más flexible con marcas/platillos específicos) y devuelve un JSON con calorías,
          macros, micronutrientes y la fuente exacta en los campos "source"/"sourceUrl"; (3) si
          ninguna de las dos está disponible o falla, usa "search_food" (base de datos Open Food
          Facts) como último recurso. No inventes datos si tienes una herramienta disponible
          para buscarlos.
        - CITA LA FUENTE SIEMPRE que hayas buscado en internet: cuando respondas la
          pregunta del usuario sobre calorías/nutrición de un alimento, menciona
          explícitamente de dónde salió el dato usando el campo "source" que te devuelve
          "search_foods_edamam"/"search_foods_bing" (ej. "Según el sitio oficial de McDonald's,
          un Big Mac tiene ~550 kcal."). Si "source" viene null o usaste tu propio conocimiento
          porque la búsqueda falló, dilo explícitamente (ej. "esto es una estimación, no
          encontré una fuente verificable"). Nunca presentes un dato buscado en la web sin decir
          de dónde salió.
        - REGLA CRÍTICA, sin excepciones: CADA VEZ que tu respuesta incluya una pregunta
          ofreciendo agregar/registrar el alimento al consumo del usuario (ej. "¿Quieres que
          lo agregue a tu consumo de hoy?", "¿Lo registro?"), sin importar si la pregunta
          original del usuario fue una consulta puramente informativa (ej. "¿cuántas
          calorías tiene un plato de arroz con huevo?") o un reporte de que ya lo comió,
          DEBES llamar a "propose_meal_for_confirmation" con esos mismos datos nutricionales
          EN ESE MISMO TURNO, antes de enviar tu respuesta de texto. Nunca escribas esa
          pregunta de confirmación sin haber llamado primero a esa herramienta - la interfaz
          del usuario depende exclusivamente de esa llamada para mostrar los botones de
          confirmación; si no la llamas, el usuario no tendrá forma de confirmar con un
          clic.

        - FLUJO OBLIGATORIO cuando el usuario diga que consumió/comió algo (ej. "hoy comí
          una banana de 90 calorías", "me comí una manzana"), en DOS pasos - NUNCA llames a
          "log_meal" en el mismo turno en que el usuario reporta la comida:
          1) Primero, SIEMPRE usa "search_food_catalog" (nuestro propio catálogo de
             productos) para ver si ese alimento/producto ya fue escaneado antes por algún
             usuario. Si hay coincidencia, ESA ES LA FUENTE DE VERDAD: usa esos datos
             directamente (son de la etiqueta real, no una estimación) y NO llames a otra
             herramienta de búsqueda para ese alimento. Solo si NO hay ninguna coincidencia en
             el catálogo, busca datos reales y actualizados de ese alimento con
             "search_foods_edamam" (prefiérela, es rápida) y, si no está disponible/falla/no
             reconoce el alimento, con "search_foods_bing" - incluso si el usuario ya te dio un
             número de calorías. No confíes en el número que dio el usuario ni en tu propio
             conocimiento como fuente final: la búsqueda es la fuente de verdad para evitar
             alucinar datos cuando el catálogo no tiene el producto. Si ninguna de las dos
             búsquedas está disponible o falla, usa "search_food" como respaldo; si ninguna
             funciona, dilo explícitamente y usa tu mejor estimación dejando claro que es
             aproximada.
          2) Con esos datos, responde al usuario confirmando qué entendiste que comió y
             muéstrale lo esencial (calorías, y cuando existan proteína, carbohidratos,
             grasa y algún micronutriente relevante como potasio o sodio), llama a
             "propose_meal_for_confirmation" con esos mismos datos (para que la interfaz le
             muestre botones de confirmación), y PREGÚNTALE explícitamente si quiere que lo
             agregues a su registro de consumo (ej. "¿Quieres que lo agregue a tu consumo de
             hoy?"). NO llames a "log_meal" todavía.
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
          pollo y ensalada"), pasa TODOS los componentes juntos en el arreglo
          "foodDescriptions" de "search_foods_edamam" (o "search_foods_bing" si esa no está
          disponible) en UNA SOLA LLAMADA (ej. foodDescriptions: ["pan", "mantequilla"]) - la
          herramienta ya busca todos los alimentos internamente en una sola solicitud, así que
          llamarla varias veces por separado (una por componente) sólo hace la respuesta más
          lenta sin ningún beneficio; NUNCA la llames una vez por ingrediente. Solo vuelve a
          llamarla en un turno posterior si de verdad depende del resultado de la primera (ej.
          necesitas confirmar qué es un ingrediente ambiguo antes de saber qué buscar). Para
          "search_foods_edamam" específicamente, cada elemento debe ir EN INGLÉS y en formato
          conciso "<cantidad><unidad> <alimento>" (ej. "200g cooked white rice", "2 large fried
          eggs") - nunca una frase descriptiva larga, ya que confunde la búsqueda con platillos
          de nombre similar. Al llamar a "log_meal", el parámetro "sourceBreakdown" es
          OBLIGATORIO, nunca lo omitas ni lo dejes vacío: llénalo con un desglose legible por
          ingrediente y su fuente ESPECÍFICA (el campo "source" devuelto por la búsqueda, ej.
          "Edamam Food Database", "Sitio oficial de McDonald's", "USDA FoodData Central"), nunca
          algo genérico - ej. "Pan: 80 kcal, 3g proteína (USDA FoodData Central); Mantequilla:
          40 kcal, 4.5g grasa (estimado)". Si es un solo alimento simple, escribe igual una
          frase corta con su fuente específica (ej. "Big Mac de McDonald's: 550 kcal (Sitio
          oficial de McDonald's)"). Si la búsqueda no trajo un "source" claro, indica
          "estimado" en vez de inventar una fuente.
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
          historial, dilo y trata la comida como nueva (busca con search_foods_edamam/search_foods_bing).
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
    private readonly EdamamFoodSearchProvider _edamamFoodSearchProvider;
    private readonly AgentProgressTracker _progressTracker;
    private readonly PendingMealTracker _pendingMealTracker;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DietAgent> _logger;
    private readonly SemaphoreSlim _mcpToolsInitLock = new(1, 1);
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private IList<AITool>? _mcpTools;

    public DietAgent(
        IConfiguration configuration,
        FoodMcpClientProvider mcpClientProvider,
        BingFoodSearchProvider bingFoodSearchProvider,
        EdamamFoodSearchProvider edamamFoodSearchProvider,
        AgentProgressTracker progressTracker,
        PendingMealTracker pendingMealTracker,
        IServiceProvider serviceProvider,
        ILogger<DietAgent> logger)
    {
        _mcpClientProvider = mcpClientProvider;
        _bingFoodSearchProvider = bingFoodSearchProvider;
        _edamamFoodSearchProvider = edamamFoodSearchProvider;
        _progressTracker = progressTracker;
        _pendingMealTracker = pendingMealTracker;
        _serviceProvider = serviceProvider;
        _logger = logger;

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

    public async Task<string> AskAsync(string prompt, string sessionId, string? azureObjectId, string? userName = null, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("DietAgent is not configured (missing Azure OpenAI settings).");
        }

        // Fast path: most turns are "what/how much did I eat" style questions about one or a
        // few concrete foods. Handling those with 3 small, specialized steps (extract -> look
        // up -> compose+propose) instead of routing them through the big multi-tool
        // ChatClientAgent below cuts latency from ~80s to ~3-5s (measured), since the slow path
        // pays for several sequential tool-decision round-trips through a huge instructions
        // block. Anything that isn't a clear food lookup (recent-meal references, free-text "sí"
        // confirmations, general advice) falls through to the full agent unchanged, so it keeps
        // using AgentSession memory/tools as before.
        var fastPathReply = await TryFastFoodLookupAsync(prompt, sessionId, azureObjectId, cancellationToken);
        if (fastPathReply is not null)
        {
            return fastPathReply;
        }

        var mcpTools = await GetOrCreateMcpToolsAsync(cancellationToken);
        // Wrapped in lambdas capturing azureObjectId (this request's caller identity) instead
        // of passing the method group directly, so the tool's exposed JSON schema/args stay
        // unchanged for the model while the DB writes/reads below scope to the correct
        // authenticated user's own Person row.
        var logMealTool = AIFunctionFactory.Create(
            (string mealType, string description, string? servingSize, double? calories, double? proteinGrams,
                double? carbsGrams, double? fatGrams, double? saturatedFatGrams, double? sugarGrams, double? fiberGrams,
                double? sodiumMilligrams, double? potassiumMilligrams, double? calciumMilligrams, double? ironMilligrams,
                double? magnesiumMilligrams, double? vitaminAMicrograms, string? consumedAtIso, string? sourceBreakdown,
                CancellationToken ct) =>
                LogMealAsync(azureObjectId, mealType, description, servingSize, calories, proteinGrams, carbsGrams,
                    fatGrams, saturatedFatGrams, sugarGrams, fiberGrams, sodiumMilligrams, potassiumMilligrams,
                    calciumMilligrams, ironMilligrams, magnesiumMilligrams, vitaminAMicrograms, consumedAtIso,
                    sourceBreakdown, ct),
            "log_meal",
            "Registra una comida consumida por el usuario en su historial (base de datos), con toda la " +
            "información nutricional disponible, incluyendo un desglose por ingrediente en 'sourceBreakdown' " +
            "cuando la comida tenga varios componentes. SOLO debe llamarse después de haber mostrado los datos " +
            "nutricionales (idealmente obtenidos con search_foods_edamam/search_foods_bing) y de que el usuario haya confirmado " +
            "explícitamente que quiere agregarlo a su registro - nunca en el mismo turno en que reporta la comida.");
        var getRecentMealsTool = AIFunctionFactory.Create(
            (int? daysBack, CancellationToken ct) => GetRecentMealsAsync(azureObjectId, daysBack, ct),
            "get_recent_meals",
            "Devuelve el historial reciente de comidas YA registradas por el usuario (fecha, descripción, " +
            "porción y datos nutricionales completos), para cuando el usuario se refiera a una comida pasada " +
            "en vez de describirla de nuevo (ej. 'lo mismo que ayer', 'los huevos de la semana pasada').");
        var searchFoodCatalogTool = AIFunctionFactory.Create(SearchFoodCatalogAsync, "search_food_catalog",
            "Busca en NUESTRO PROPIO catálogo de productos (base de datos global compartida por todos los " +
            "usuarios, alimentada al escanear etiquetas de nutrición reales) por nombre o marca. Devuelve un " +
            "JSON con los productos que coincidan (calorías, macros y micronutrientes ya verificados de la " +
            "etiqueta). ÚSALA SIEMPRE PRIMERO, antes de search_foods_edamam/search_foods_bing, ya que es instantánea y sus datos " +
            "vienen de una etiqueta real en vez de una búsqueda web.");
        var proposeMealTool = AIFunctionFactory.Create(
            (string mealType, string description, string? servingSize, double? calories, double? proteinGrams,
                double? carbsGrams, double? fatGrams, double? saturatedFatGrams, double? sugarGrams, double? fiberGrams,
                double? sodiumMilligrams, double? potassiumMilligrams, double? calciumMilligrams, double? ironMilligrams,
                double? magnesiumMilligrams, double? vitaminAMicrograms, string? consumedAtIso, string? sourceBreakdown) =>
            {
                _pendingMealTracker.Set(sessionId, new PendingMealDto(mealType, description, servingSize, calories,
                    proteinGrams, carbsGrams, fatGrams, saturatedFatGrams, sugarGrams, fiberGrams, sodiumMilligrams,
                    potassiumMilligrams, calciumMilligrams, ironMilligrams, magnesiumMilligrams, vitaminAMicrograms,
                    consumedAtIso, sourceBreakdown));
                return "Datos capturados para que la interfaz le muestre botones de confirmación al usuario.";
            },
            "propose_meal_for_confirmation",
            "Llama a esta herramienta UNA VEZ, JUSTO DESPUÉS de mostrarle al usuario el desglose nutricional " +
            "completo de una comida y EN EL MISMO TURNO en que le preguntas si quiere agregarla - usa los " +
            "MISMOS datos/parámetros que usarías para 'log_meal', para que la interfaz le muestre botones de " +
            "confirmación en vez de que tenga que escribir 'sí'. Esto NO registra la comida ni reemplaza tu " +
            "pregunta de texto normal - sigue preguntando igual. No la llames para comidas ya registradas ni " +
            "para respuestas que no proponen una comida nueva.");

        IList<AITool> tools = [.. mcpTools, logMealTool, getRecentMealsTool, searchFoodCatalogTool, proposeMealTool];

        if (_edamamFoodSearchProvider.IsConfigured)
        {
            // Direct structured nutrition API (single HTTP call, no LLM-agent thread/run cycle)
            // - resolves in 1-3s, so this is preferred over search_foods_bing when available.
            var edamamFoodsTool = AIFunctionFactory.Create(
                async (
                    [Description("Alimentos a buscar, uno por elemento, EN INGLÉS y en formato conciso \"<cantidad><unidad> <alimento>\" (ej. [\"200g cooked white rice\", \"2 large fried eggs\"]) - NUNCA frases descriptivas largas (ej. NO \"a large plate of rice\"), ya que confunden la búsqueda con platillos de nombre similar. Incluye TODOS los componentes de la comida en esta única llamada, incluso si es un solo alimento.")] string[] foodDescriptions,
                    CancellationToken ct) => SearchFoodsEdamamAsync(foodDescriptions, sessionId, ct),
                "search_foods_edamam",
                "Busca en la API estructurada de nutrición de Edamam (rápida, sin búsqueda web) la información " +
                "nutricional completa de uno o varios alimentos EN UNA SOLA LLAMADA: calorías, macros y " +
                "micronutrientes comunes. Úsala cuando search_foods_dietly no está disponible o si los resultados " +
                "no tienen datos completos - solo usa search_foods_bing si Edamam tampoco funciona. Devuelve un arreglo " +
                "JSON en el mismo orden que 'foodDescriptions', cada elemento con un campo 'query' que repite " +
                "el alimento buscado.");
            tools = [.. tools, edamamFoodsTool];
        }

        if (_bingFoodSearchProvider.IsConfigured)
        {
            // Takes ALL of a meal's ingredients in ONE call (array, even for a single food) so
            // they're resolved in a single Bing agent thread/run instead of one per ingredient -
            // halves the thread/run overhead on top of them already resolving concurrently.
            var bingFoodsTool = AIFunctionFactory.Create(
                async (
                    [Description("Alimentos a buscar, uno por elemento (ej. [\"arroz blanco cocido 200g\", \"huevo frito\"]). Incluye TODOS los componentes de la comida en esta única llamada, incluso si es un solo alimento (arreglo de un elemento).")] string[] foodDescriptions,
                    CancellationToken ct) => SearchFoodsBingAsync(foodDescriptions, sessionId, ct),
                "search_foods_bing",
                "Busca en la web (Bing) la información nutricional completa de uno o varios alimentos EN UNA " +
                "SOLA LLAMADA: calorías, macros (proteína, carbohidratos, grasa total, grasa saturada, " +
                "azúcares, fibra) y micronutrientes comunes (sodio, potasio, calcio, hierro, magnesio, " +
                "vitamina A) de cada uno. Devuelve un arreglo JSON en el mismo orden que 'foodDescriptions', " +
                "cada elemento con un campo 'query' que repite el alimento buscado. Si la comida tiene varios " +
                "componentes, pásalos TODOS juntos en 'foodDescriptions' en esta única llamada, nunca uno por " +
                "llamada.");
            tools = [.. tools, bingFoodsTool];
        }

        // A plain AsAIAgent(tools:) call wraps the chat client with a FunctionInvokingChatClient
        // that runs multiple same-turn tool calls SEQUENTIALLY. search_foods_bing now handles a
        // whole meal's ingredients in one call/one Bing thread, but AllowConcurrentInvocation is
        // still useful for other same-turn tool combos; UseProvidedChatClientAsIs=true stops
        // ChatClientAgent from wrapping it again with its own (sequential) function-invocation layer.
        IChatClient chatClient = _chatClient.AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation(configure: fic => fic.AllowConcurrentInvocation = true)
            .Build();
        var agent = chatClient.AsAIAgent(new ChatClientAgentOptions
        {
            Name = "DietAgent",
            ChatOptions = new ChatOptions { Instructions = Instructions, Tools = tools },
            UseProvidedChatClientAsIs = true,
        });
        var session = await GetOrCreateSessionAsync(agent, sessionId, cancellationToken);

        var skill = DietSkillSelector.Select(prompt);
        var skillGuidance = DietSkillLibrary.InstructionsFor(skill);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTime.UtcNow, MealTimeHelper.Central);
        var userLine = string.IsNullOrWhiteSpace(userName) ? string.Empty : $"[Usuario: {userName}]\n";
        var fullPrompt = $"{userLine}[Fecha y hora actual: {nowLocal:yyyy-MM-dd HH:mm} ({nowLocal:dddd})]\n" +
            $"[Guía de skill: {skillGuidance}]\n\nPregunta del usuario: {prompt}";

        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);
        return response.Text;
    }

    private sealed record FoodClassifyResult(bool IsFoodQuery, string[]? Foods, string[]? OriginalFoods, bool AlreadyConsumed, string? MealType);

    private sealed record ComposeMealResult(
        string ReplyText, string? MealType, string? Description, string? ServingSize,
        double? Calories, double? ProteinGrams, double? CarbsGrams, double? FatGrams,
        double? SaturatedFatGrams, double? SugarGrams, double? FiberGrams, double? SodiumMilligrams,
        double? PotassiumMilligrams, double? CalciumMilligrams, double? IronMilligrams,
        double? MagnesiumMilligrams, double? VitaminAMicrograms, string? SourceBreakdown);

    private static readonly JsonSerializerOptions FastPathJsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Specialized 3-step pipeline for "look up nutrition of concrete food(s)" turns, the
    /// slowest and most common case handled by the full agent below (~80s measured, mostly
    /// LLM tool-decision round-trip overhead, not the search itself). Step 1: a single small,
    /// tool-less LLM call classifies the turn and extracts the food list. Step 2: deterministic
    /// C# looks each one up (catalog DB first, Edamam for the rest) - no LLM involved. Step 3:
    /// a second small, tool-less LLM call composes the reply and returns the structured meal
    /// totals directly, which are set on <see cref="_pendingMealTracker"/> without needing a
    /// "propose_meal_for_confirmation" tool call. Returns null (meaning "not handled, fall back
    /// to the full agent") when the turn isn't a clear food lookup, or when neither the catalog
    /// nor Edamam found anything, so the slow path's Bing fallback still gets a chance.
    /// </summary>
    private async Task<string?> TryFastFoodLookupAsync(string prompt, string sessionId, string? azureObjectId, CancellationToken cancellationToken)
    {
        if (!_edamamFoodSearchProvider.IsConfigured)
        {
            // The whole point of the fast path is Edamam's speed; without it, let the full
            // agent's search_foods_bing/search_food fallbacks handle everything as before.
            return null;
        }

        FoodClassifyResult? classify;
        try
        {
            var classifyResponse = await _chatClient!.CompleteChatAsync(
                [
                    new SystemChatMessage(
                        "Eres un clasificador para un agente de nutrición. Analiza el mensaje del usuario y " +
                        "determina si menciona alimento(s) CONCRETOS cuyo valor nutricional se pueda buscar " +
                        "(para responder una pregunta puntual o porque reporta haberlo comido), a diferencia de " +
                        "preguntas generales, consejos, referencias a comidas pasadas ('lo mismo que ayer'), " +
                        "confirmaciones tipo 'sí'/'no'/'dale', o temas no relacionados con un alimento concreto. " +
                        "Responde SOLO con JSON: {\"isFoodQuery\": bool, \"foods\": [\"alimento en INGLÉS, " +
                        "formato conciso '<cantidad><unidad> <alimento>' ej. '150g grilled salmon'\"], " +
                        "\"originalFoods\": [\"el mismo alimento pero tal cual lo mencionó el usuario, en su " +
                        "idioma original y SIN traducir, ej. 'sopa de arroz con mole' - mismo orden y misma " +
                        "cantidad de elementos que 'foods'\"], " +
                        "\"alreadyConsumed\": bool (true si el usuario dice que ya lo comió), " +
                        "\"mealType\": \"breakfast\"|\"lunch\"|\"dinner\"|\"snack\"}. Si isFoodQuery es false, " +
                        "deja foods y originalFoods como arreglos vacíos."),
                    new UserChatMessage(prompt),
                ],
                new ChatCompletionOptions { ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonObjectFormat() },
                cancellationToken);
            classify = JsonSerializer.Deserialize<FoodClassifyResult>(classifyResponse.Value.Content[0].Text, FastPathJsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        if (classify is null || !classify.IsFoodQuery || classify.Foods is null || classify.Foods.Length == 0)
        {
            return null;
        }

        var dbFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
        int? personId = null;
        if (dbFactory is not null)
        {
            var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
            if (personProvider is not null)
            {
                personId = await personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);
            }
        }

        var nutritionItems = new JsonArray();
        var unmatched = new List<string>();
        for (var i = 0; i < classify.Foods.Length; i++)
        {
            var food = classify.Foods[i];
            // Catalog entries are saved in whatever language the user said them in (ej.
            // "sopa de arroz con mole"), while 'food' here is the English translation Edamam
            // needs - so catalog lookups must use the ORIGINAL phrase instead, falling back to
            // the English one if the classify step didn't return a matching originalFoods entry.
            var catalogQuery = classify.OriginalFoods is not null && i < classify.OriginalFoods.Length && !string.IsNullOrWhiteSpace(classify.OriginalFoods[i])
                ? classify.OriginalFoods[i]
                : food;

            // Personal catalog (this user's own previously-saved items) first, since it's an
            // exact match to something they already confirmed before - then our shared
            // label-scanned catalog, then Edamam as the last resort.
            var personalMatch = dbFactory is null || personId is null
                ? null
                : await TryGetPersonalCatalogNutritionAsync(dbFactory, personId.Value, catalogQuery, cancellationToken);
            var catalogMatch = personalMatch ?? (dbFactory is null ? null : await TryGetCatalogNutritionAsync(dbFactory, catalogQuery, cancellationToken));
            if (catalogMatch is not null)
            {
                nutritionItems.Add(catalogMatch);
            }
            else
            {
                unmatched.Add(food);
            }
        }

        if (unmatched.Count > 0)
        {
            var edamamJson = await _edamamFoodSearchProvider.SearchFoodsNutritionJsonAsync(unmatched, cancellationToken);
            if (edamamJson is not null && JsonNode.Parse(edamamJson) is JsonArray edamamArray)
            {
                var length = Math.Min(edamamArray.Count, unmatched.Count);
                for (var i = 0; i < length; i++)
                {
                    if (edamamArray[i] is JsonObject obj && obj.ContainsKey("calories"))
                    {
                        nutritionItems.Add(obj.DeepClone());
                        _ = CacheBingResultAsync(unmatched[i], obj.ToJsonString(), CancellationToken.None);
                    }
                }
            }
        }

        if (nutritionItems.Count == 0)
        {
            // Neither the catalog nor Edamam recognized any of the foods - fall back to the
            // full agent so its search_foods_bing/search_food last-resort tools get a try.
            return null;
        }

        ComposeMealResult? composed;
        try {
            var nowLocal = TimeZoneInfo.ConvertTime(DateTime.UtcNow, MealTimeHelper.Central);
            var composeResponse = await _chatClient!.CompleteChatAsync(
                [
                    new SystemChatMessage(
                        "Con estos datos nutricionales en JSON (uno por alimento, cada uno con su 'source'), " +
                        "calcula los TOTALES sumando todos los alimentos y redacta una respuesta breve en " +
                        "español (campo 'replyText') citando la(s) fuente(s) por alimento (ej. 'Según Edamam " +
                        "Food Database...'; si un alimento vino de 'source' null o de nuestro catálogo, dilo " +
                        "explícitamente). Si 'alreadyConsumed' es true, usa tono de pasado y SIEMPRE pregunta " +
                        "explícitamente si quiere agregarlo a su registro de hoy (ej. '¿Quieres que lo agregue " +
                        "a tu consumo de hoy?'); si es false, responde la pregunta informativa y de todas " +
                        "formas pregunta si quiere agregarlo. Responde SOLO con JSON: {\"replyText\": string, " +
                        "\"mealType\": \"breakfast\"|\"lunch\"|\"dinner\"|\"snack\", \"description\": string " +
                        "(breve, ej. 'salmón a la plancha y banana'), \"servingSize\": string|null, " +
                        "\"calories\": number|null, \"proteinGrams\": number|null, \"carbsGrams\": number|null, " +
                        "\"fatGrams\": number|null, \"saturatedFatGrams\": number|null, \"sugarGrams\": " +
                        "number|null, \"fiberGrams\": number|null, \"sodiumMilligrams\": number|null, " +
                        "\"potassiumMilligrams\": number|null, \"calciumMilligrams\": number|null, " +
                        "\"ironMilligrams\": number|null, \"magnesiumMilligrams\": number|null, " +
                        "\"vitaminAMicrograms\": number|null, \"sourceBreakdown\": string (desglose legible " +
                        "por alimento y su fuente específica, ej. 'Salmón: 313 kcal (Edamam Food Database); " +
                        "Banana: 102 kcal (Edamam Food Database)')} - todos los totales son la SUMA de todos " +
                        "los alimentos en los datos."),
                    new UserChatMessage(
                        $"[Fecha y hora actual: {nowLocal:yyyy-MM-dd HH:mm} ({nowLocal:dddd})]\n" +
                        $"[alreadyConsumed: {classify.AlreadyConsumed}]\n[mealType sugerido: {classify.MealType ?? "snack"}]\n\n" +
                        $"Pregunta original del usuario: {prompt}\n\nDatos nutricionales JSON: {nutritionItems.ToJsonString()}"),
                ],
                new ChatCompletionOptions { ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonObjectFormat() },
                cancellationToken);
            composed = JsonSerializer.Deserialize<ComposeMealResult>(composeResponse.Value.Content[0].Text, FastPathJsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        if (composed is null || string.IsNullOrWhiteSpace(composed.ReplyText))
        {
            return null;
        }

        // Deterministic "insert" step - equivalent to the full agent's
        // propose_meal_for_confirmation tool, but just parsing step 3's own structured output
        // instead of a separate tool-call round-trip.
        _pendingMealTracker.Set(sessionId, new PendingMealDto(
            composed.MealType ?? classify.MealType ?? "snack",
            composed.Description ?? classify.Foods[0],
            composed.ServingSize,
            composed.Calories,
            composed.ProteinGrams,
            composed.CarbsGrams,
            composed.FatGrams,
            composed.SaturatedFatGrams,
            composed.SugarGrams,
            composed.FiberGrams,
            composed.SodiumMilligrams,
            composed.PotassiumMilligrams,
            composed.CalciumMilligrams,
            composed.IronMilligrams,
            composed.MagnesiumMilligrams,
            composed.VitaminAMicrograms,
            ConsumedAtIso: null,
            composed.SourceBreakdown));

        return composed.ReplyText;
    }

    /// <summary>Best-effort single best match from THIS person's own saved catalog (see
    /// PersonalFoodItem/PersonalFoodCatalogHelper) for one food description, normalized to the
    /// same field shape Edamam's provider returns - null if nothing matches. Checked before the
    /// shared label-scanned catalog since it's the user's own previously-confirmed data.</summary>
    private static async Task<JsonObject?> TryGetPersonalCatalogNutritionAsync(
        IDbContextFactory<PersonalAgentDbContext> dbFactory, int personId, string foodDescription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(foodDescription))
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var match = await PersonalFoodCatalogHelper.FindBestWordMatchAsync(db, personId, foodDescription, cancellationToken);

        return match is null
            ? null
            : new JsonObject
            {
                ["query"] = foodDescription,
                ["servingSize"] = match.ServingSize,
                ["calories"] = match.Calories,
                ["proteinGrams"] = match.ProteinGrams,
                ["carbsGrams"] = match.CarbsGrams,
                ["fatGrams"] = match.FatGrams,
                ["saturatedFatGrams"] = match.SaturatedFatGrams,
                ["sugarGrams"] = match.SugarGrams,
                ["fiberGrams"] = match.FiberGrams,
                ["sodiumMilligrams"] = match.SodiumMilligrams,
                ["potassiumMilligrams"] = match.PotassiumMilligrams,
                ["calciumMilligrams"] = match.CalciumMilligrams,
                ["ironMilligrams"] = match.IronMilligrams,
                ["magnesiumMilligrams"] = match.MagnesiumMilligrams,
                ["vitaminAMicrograms"] = match.VitaminAMicrograms,
                ["source"] = "Tu catálogo personal",
            };
    }

    /// <summary>Best-effort single best match from our own GLOBAL catalog for one food
    /// description, using the same word-overlap logic as the personal catalog (see
    /// FoodCatalogMatcher) instead of an exact substring LIKE - normalized to the same field
    /// shape Edamam's provider returns (so both can be merged into one nutrition-items array) -
    /// null if nothing matches.</summary>
    private static async Task<JsonObject?> TryGetCatalogNutritionAsync(
        IDbContextFactory<PersonalAgentDbContext> dbFactory, string foodDescription, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(foodDescription))
        {
            return null;
        }

        var queryWords = FoodCatalogMatcher.SignificantWords(foodDescription);
        if (queryWords.Length == 0)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.FoodItems.ToListAsync(cancellationToken);
        var match = FoodCatalogMatcher.PickBestMatch(candidates, queryWords, f => $"{f.Name} {f.Brand}", f => f.TimesLogged);

        return match is null
            ? null
            : new JsonObject
            {
                ["query"] = foodDescription,
                ["servingSize"] = match.ServingSize,
                ["calories"] = match.Calories,
                ["proteinGrams"] = match.ProteinGrams,
                ["carbsGrams"] = match.CarbsGrams,
                ["fatGrams"] = match.FatGrams,
                ["saturatedFatGrams"] = match.SaturatedFatGrams,
                ["sugarGrams"] = match.SugarGrams,
                ["fiberGrams"] = match.FiberGrams,
                ["sodiumMilligrams"] = match.SodiumMilligrams,
                ["potassiumMilligrams"] = match.PotassiumMilligrams,
                ["calciumMilligrams"] = match.CalciumMilligrams,
                ["ironMilligrams"] = match.IronMilligrams,
                ["magnesiumMilligrams"] = match.MagnesiumMilligrams,
                ["vitaminAMicrograms"] = match.VitaminAMicrograms,
                ["source"] = "Nuestro catálogo de productos",
            };
    }

    /// <summary>Looks up one or several foods via Edamam's structured nutrition API in a single
    /// call, publishing a progress line and caching the result for each one.</summary>
    private async Task<string> SearchFoodsEdamamAsync(string[] foodDescriptions, string sessionId, CancellationToken cancellationToken)
    {
        if (foodDescriptions.Length == 0)
        {
            return "[]";
        }

        var json = await _edamamFoodSearchProvider.SearchFoodsNutritionJsonAsync(foodDescriptions, cancellationToken);
        if (json is null)
        {
            return "No se encontraron resultados en Edamam para esos alimentos.";
        }

        PublishAndCacheFoodResults(foodDescriptions, json, sessionId);
        return json;
    }

    /// <summary>Looks up one or several foods in a single Bing agent call, publishing a progress
    /// line and caching the result for each one as it's parsed out of the returned JSON array
    /// (or single object, if the model didn't wrap a lone result in an array).</summary>
    private async Task<string> SearchFoodsBingAsync(string[] foodDescriptions, string sessionId, CancellationToken cancellationToken)
    {
        if (foodDescriptions.Length == 0)
        {
            return "[]";
        }

        var json = foodDescriptions.Length == 1
            ? await _bingFoodSearchProvider.SearchFoodNutritionJsonAsync(foodDescriptions[0], cancellationToken)
            : await _bingFoodSearchProvider.SearchFoodsNutritionJsonAsync(foodDescriptions, cancellationToken);
        if (json is null)
        {
            return "No se encontraron resultados en Bing para esos alimentos.";
        }

        PublishAndCacheFoodResults(foodDescriptions, json, sessionId);
        return json;
    }

    /// <summary>Shared by both search_foods_edamam and search_foods_bing: parses a returned JSON
    /// array (or single object, if the model didn't wrap a lone result in an array), publishing a
    /// progress line and caching each item into the FoodItems catalog.</summary>
    private void PublishAndCacheFoodResults(string[] foodDescriptions, string json, string sessionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var length = Math.Min(doc.RootElement.GetArrayLength(), foodDescriptions.Length);
                for (var i = 0; i < length; i++)
                {
                    var itemJson = doc.RootElement[i].GetRawText();
                    _progressTracker.Publish(sessionId, BuildProgressLine(foodDescriptions[i], itemJson));
                    _ = CacheBingResultAsync(foodDescriptions[i], itemJson, CancellationToken.None);
                }
            }
            else
            {
                // Model returned a single object even though (possibly) several foods were
                // asked for - still usable for the common one-ingredient case.
                _progressTracker.Publish(sessionId, BuildProgressLine(foodDescriptions[0], json));
                _ = CacheBingResultAsync(foodDescriptions[0], json, CancellationToken.None);
            }
        }
        catch (JsonException)
        {
            // Not parseable JSON (e.g. a plain "no results" message) - nothing to cache/publish,
            // the raw text is still returned to the model to see.
        }
    }

    /// <summary>Best-effort: parses one search result item's JSON and, if it found real data
    /// (calories present), upserts it into the shared FoodItems catalog keyed by the searched
    /// phrase - never overwrites an existing entry (a real nutrition-label scan is more
    /// authoritative than a search estimate). Any failure here is swallowed since this is purely a
    /// speed optimization.</summary>
    private async Task CacheBingResultAsync(string foodDescription, string json, CancellationToken cancellationToken)
    {
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
        if (dbContextFactory is null)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            double? GetNumber(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDouble() : null;
            string? GetString(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

            var calories = GetNumber("calories");
            if (calories is null)
            {
                return;
            }

            var matchKey = $"{foodDescription.Trim().ToLowerInvariant()}|";
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            if (await db.FoodItems.AnyAsync(f => f.MatchKey == matchKey, cancellationToken))
            {
                return;
            }

            db.FoodItems.Add(new FoodItem
            {
                Name = foodDescription.Trim(),
                ServingSize = GetString("servingSize"),
                Calories = calories,
                ProteinGrams = GetNumber("proteinGrams"),
                CarbsGrams = GetNumber("carbsGrams"),
                FatGrams = GetNumber("fatGrams"),
                SaturatedFatGrams = GetNumber("saturatedFatGrams"),
                SugarGrams = GetNumber("sugarGrams"),
                FiberGrams = GetNumber("fiberGrams"),
                SodiumMilligrams = GetNumber("sodiumMilligrams"),
                PotassiumMilligrams = GetNumber("potassiumMilligrams"),
                CalciumMilligrams = GetNumber("calciumMilligrams"),
                IronMilligrams = GetNumber("ironMilligrams"),
                MagnesiumMilligrams = GetNumber("magnesiumMilligrams"),
                VitaminAMicrograms = GetNumber("vitaminAMicrograms"),
                MatchKey = matchKey,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (JsonException)
        {
            // Not parseable JSON (e.g. a plain "no results" message) - nothing to cache.
        }
        catch (DbUpdateException)
        {
            // Unique-index race with a concurrent identical search - safe to ignore.
        }
    }

    /// <summary>Builds a short "ingrediente: X kcal (fuente)" line for AgentProgressTracker from one search_foods_bing result item's raw JSON - falls back to a generic line if it's not parseable JSON (e.g. a "no results" message).</summary>
    private static string BuildProgressLine(string foodDescription, string result)
    {
        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;
            var calories = root.TryGetProperty("calories", out var caloriesEl) && caloriesEl.ValueKind == JsonValueKind.Number
                ? caloriesEl.GetDouble().ToString("0")
                : null;
            var source = root.TryGetProperty("source", out var sourceEl) && sourceEl.ValueKind == JsonValueKind.String
                ? sourceEl.GetString()
                : null;
            return calories is not null
                ? $"{foodDescription}: {calories} kcal{(source is not null ? $" ({source})" : string.Empty)}"
                : $"{foodDescription}: no se encontró información nutricional.";
        }
        catch (JsonException)
        {
            return $"{foodDescription}: no se encontró información nutricional.";
        }
    }

    private async Task<string> SearchFoodCatalogAsync(
        [Description("Nombre o marca del producto a buscar en nuestro catálogo propio, ej. 'Coca-Cola' o 'yogurt griego Chobani'.")] string foodDescription,
        CancellationToken cancellationToken)
    {
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
        if (dbContextFactory is null || string.IsNullOrWhiteSpace(foodDescription))
        {
            return "El catálogo de productos no está disponible.";
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pattern = $"%{foodDescription.Trim()}%";
        var matches = await db.FoodItems
            .Where(f => EF.Functions.Like(f.Name, pattern) || (f.Brand != null && EF.Functions.Like(f.Brand, pattern)))
            .OrderByDescending(f => f.TimesLogged)
            .Take(5)
            .Select(f => new
            {
                f.Name,
                f.Brand,
                f.ServingSize,
                f.Calories,
                f.ProteinGrams,
                f.CarbsGrams,
                f.FatGrams,
                f.SaturatedFatGrams,
                f.SugarGrams,
                f.FiberGrams,
                f.SodiumMilligrams,
                f.PotassiumMilligrams,
                f.CalciumMilligrams,
                f.IronMilligrams,
                f.MagnesiumMilligrams,
                f.VitaminAMicrograms,
            })
            .ToListAsync(cancellationToken);

        return matches.Count == 0
            ? "No se encontraron productos con ese nombre/marca en nuestro catálogo propio."
            : JsonSerializer.Serialize(matches);
    }

    private async Task<string> GetRecentMealsAsync(
        string? azureObjectId,
        [Description("Días hacia atrás a buscar en el historial, ej. 1 para 'ayer', 7 para 'la semana pasada'. Si no se especifica, usa 14.")] int? daysBack,
        CancellationToken cancellationToken)
    {
        var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();

        if (personProvider is null || dbContextFactory is null)
        {
            return "No se pudo consultar el historial: la base de datos no está configurada.";
        }

        var personId = await personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);
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
            if (_mcpTools is not null)
            {
                return _mcpTools;
            }

            try
            {
                _mcpTools = await _mcpClientProvider.GetToolsAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // The local Open Food Facts MCP server (spawned as a child process) is only a
                // last-resort fallback behind search_food_catalog/search_foods_edamam/
                // search_foods_bing, and isn't available in every deployment environment (e.g.
                // Azure production, where it isn't published/reachable as a child process).
                // Degrade to no MCP tools instead of failing the whole chat request.
                _logger.LogWarning(ex, "Could not connect to the Food MCP server; continuing without its tools.");
                _mcpTools = [];
            }

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
        string? azureObjectId,
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

        var personId = await personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);

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
