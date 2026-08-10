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
        Tu prioridad: buscar datos confiables, RÁPIDO, en el orden correcto.

        ⚡ ORDEN DE BÚSQUEDA POR DEFECTO:
        1️⃣ SIEMPRE intenta search_food_catalog PRIMERO (instantáneo, ya confirmado por usuario)
        2️⃣ Si no encuentra → search_foods_edamam (rápido, <1s, API estructurada)
        3️⃣ Si aún no tiene datos → search_foods_bing (web exhaustiva, 5-15s, último recurso)

        🚨 EXCEPCIÓN OBLIGATORIA, SIN EXCUSAS: Si el usuario pide EXPLÍCITAMENTE buscar "en internet",
        "en la web", "en Bing", o dice algo como "búscalo"/"consulta"/"busca en" refiriéndose a la web,
        IGNORA por completo el orden de arriba: ve DIRECTO a search_foods_bing, SIN llamar antes a
        search_food_catalog ni a search_foods_edamam, incluso si ya tienes esos datos en el catálogo o
        ya respondiste esa misma pregunta antes con el catálogo. Tu respuesta DEBE citar esa fuente web
        específica (nunca "según nuestro catálogo" en este caso) - el usuario pidió una búsqueda nueva
        en internet y espera un resultado de internet, no el dato ya conocido.

        REGLA CRÍTICA (fuera de esa excepción): No saltees directamente a Bing. El catálogo local y
        Edamam son casi siempre más rápidos. Solo usa Bing si el usuario lo pide explícitamente (ver
        excepción arriba) O si las dos anteriores no retornan datos válidos.

        CUANDO BUSQUES:
        - Siempre cita la fuente: "Según nuestro catálogo", "Según Edamam", "Según [marca oficial]", etc.
        - Valida datos con sentido común: ¿calorías razonables? ¿macros lógicos? ¿porción realista?
        - Si los datos parecen mal, dile al usuario: "Esto no me parece correcto. ¿Quieres que busque en internet?"

        FLUJO PARA REGISTRAR COMIDAS (cuando dice "comí..."):
        1) Busca datos EN ORDEN: catálogo → Edamam → Bing (solo si pide/falla lo anterior)
        2) Valida con sentido común
        3) Llama a "propose_meal_for_confirmation" y pregunta si registra
        4) SOLO después que confirme en siguiente mensaje, llama "log_meal"

        DATOS A BUSCAR: calorías, proteína, carbos, grasa, grasa saturada, fibra, azúcares,
        sodio, potasio, calcio, hierro, magnesio, vitamina A.

        FORMATO PARA EDAMAM (search_foods_edamam):
        - Usa formato CONCISO en inglés: "<cantidad><unidad> <alimento>" 
        - CORRECTO: "200g cooked white rice", "1 large apple", "2 fried eggs"
        - INCORRECTO: "una plate grande de arroz", "manzana grande que comiste"
        - Cuando el usuario mencione un alimento en español, tradúcelo a esa forma concisa en inglés
        
        - Al llamar a "log_meal", SIEMPRE incluye "sourceBreakdown": desglose legible por ingrediente
          y su fuente específica (ej. "Pan: 80 kcal (Catálogo Propio); Mantequilla: 40 kcal (Edamam)").
          Si la fuente no es clara, indica "estimado" en vez de inventar. Esto ayuda a rastrear qué
          datos vinieron de dónde y de qué búsqueda fue validada.
        
        - Si no tienes suficiente información del usuario (peso, objetivo, alergias), pregúntala antes
          de dar recomendaciones personalizadas.
        
        - Cuando el usuario se refiera a una comida pasada ("lo mismo que ayer", "mis huevos de siempre"),
          usa "get_recent_meals" para ver su historial antes de buscar en la web. Reutiliza esos datos
          exactamente (incluyendo el "sourceBreakdown" guardado) y confirma con el usuario antes de
          registrar.
        
        - No eres médico: para problemas médicos serios, recomienda consultar a un profesional.
        
        - Cada mensaje incluye la fecha y hora actual en corchetes (ej. "[Fecha y hora actual: 2026-08-03
          14:00 (lunes)]"). Úsala como referencia de "hoy" al registrar comidas.
        """;

    private readonly ChatClient? _chatClient;
    private readonly FoodMcpClientProvider _mcpClientProvider;
    private readonly BingFoodSearchProvider _bingFoodSearchProvider;
    private readonly EdamamFoodSearchProvider _edamamFoodSearchProvider;
    private readonly AgentProgressTracker _progressTracker;
    private readonly PendingMealTracker _pendingMealTracker;
    private readonly FoodSourceChoiceTracker _foodSourceChoiceTracker;
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
        FoodSourceChoiceTracker foodSourceChoiceTracker,
        IServiceProvider serviceProvider,
        ILogger<DietAgent> logger)
    {
        _mcpClientProvider = mcpClientProvider;
        _bingFoodSearchProvider = bingFoodSearchProvider;
        _edamamFoodSearchProvider = edamamFoodSearchProvider;
        _progressTracker = progressTracker;
        _pendingMealTracker = pendingMealTracker;
        _foodSourceChoiceTracker = foodSourceChoiceTracker;
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

        _logger.LogInformation($"[DietAgent.AskAsync] BingConfigured={_bingFoodSearchProvider.IsConfigured}, EdamamConfigured={_edamamFoodSearchProvider.IsConfigured}");

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
        // If user explicitly asks for Bing/internet search, skip the fast path and let the full
        // agent handle it so the LLM can obey the explicit user command
        if (prompt.Contains("bing", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("internet", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("web", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("búscalo en", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("busca en", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("consulta", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // If user explicitly asks for ONLY catalog or ONLY Edamam, skip the fast path
        // and let the full agent handle it with the appropriate tool restriction
        if (prompt.Contains("solo catálogo", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("solo local", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("mis productos", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("solo edamam", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("solo api", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("solo estructura", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!_edamamFoodSearchProvider.IsConfigured)
        {
            // The whole point of the fast path is Edamam's speed; without it, let the full
            // agent's search_foods_bing/search_food fallbacks handle everything as before.
            return null;
        }

        var classify = await ClassifyFoodQueryAsync(prompt, cancellationToken);
        if (classify is null || !classify.IsFoodQuery || classify.Foods is null || classify.Foods.Length == 0)
        {
            return null;
        }

        // Instead of auto-picking a lookup source (old behavior), always let the user choose -
        // stash the extracted food list and reply with a short prompt; the frontend renders 4
        // buttons ("Local"/"Global"/"Edamam"/"Internet") that call SearchBySpecificSourceAsync
        // below with this exact same choice once clicked.
        var originalFoods = classify.OriginalFoods is { Length: > 0 } ? classify.OriginalFoods : classify.Foods;
        _foodSourceChoiceTracker.Set(sessionId, new FoodSourceChoiceDto(
            classify.Foods,
            originalFoods,
            classify.MealType ?? "snack",
            classify.AlreadyConsumed,
            prompt));

        var foodList = string.Join(", ", originalFoods);
        return $"¿De dónde quieres que busque la información nutricional de \"{foodList}\"? " +
            "Elige una opción abajo: catálogo, Edamam o internet.";
    }

    /// <summary>Small, tool-less LLM call shared by TryFastFoodLookupAsync (chat "Enviar" flow)
    /// and SearchByPromptAndSourceAsync (permanent Local/Global/Edamam/Internet buttons flow) -
    /// classifies whether the message mentions concrete food(s) and extracts them. Returns null
    /// on any classification failure (caller decides what "not handled" means for its flow).</summary>
    private async Task<FoodClassifyResult?> ClassifyFoodQueryAsync(string prompt, CancellationToken cancellationToken)
    {
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
            return JsonSerializer.Deserialize<FoodClassifyResult>(classifyResponse.Value.Content[0].Text, FastPathJsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Backs the 4 permanent "Local"/"Global"/"Edamam"/"Internet" buttons shown next to
    /// the chat input: takes the raw text the user typed PLUS the source they explicitly picked
    /// (instead of "Enviar") in one call - classifies/extracts the food(s) from the text, then
    /// runs the search on ONLY that source, skipping the "¿de dónde busco?" round trip entirely
    /// since the user already answered that by which button they pressed.</summary>
    public async Task<(string Reply, PendingMealDto? PendingMeal)> SearchByPromptAndSourceAsync(
        string prompt, string source, string? azureObjectId, CancellationToken cancellationToken)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("DietAgent is not configured (missing Azure OpenAI settings).");
        }

        var classify = await ClassifyFoodQueryAsync(prompt, cancellationToken);
        if (classify is null || !classify.IsFoodQuery || classify.Foods is null || classify.Foods.Length == 0)
        {
            return ("No pude identificar un alimento concreto en tu mensaje. ¿Puedes describirlo de otra forma?", null);
        }

        var originalFoods = classify.OriginalFoods is { Length: > 0 } ? classify.OriginalFoods : classify.Foods;
        var choice = new FoodSourceChoiceDto(classify.Foods, originalFoods, classify.MealType ?? "snack", classify.AlreadyConsumed, prompt);
        return await SearchBySpecificSourceAsync(choice, source, azureObjectId, cancellationToken);
    }

    /// <summary>Runs the lookup for exactly ONE source the user chose from the 3-button chooser
    /// TryFastFoodLookupAsync offers, then composes the reply + a PendingMeal the same way the
    /// old auto-picking fast path used to - shared by the text chat's POST /api/foods/search-source
    /// endpoint. Returns a short "not found" reply (no PendingMeal) if that specific source had
    /// nothing, instead of falling back to another source automatically - the user picked this
    /// one on purpose and can just click a different button to try another.</summary>
    public async Task<(string Reply, PendingMealDto? PendingMeal)> SearchBySpecificSourceAsync(
        FoodSourceChoiceDto choice, string source, string? azureObjectId, CancellationToken cancellationToken)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("DietAgent is not configured (missing Azure OpenAI settings).");
        }

        var nutritionItems = new JsonArray();
        var sourceLabel = source switch
        {
            "catalog" => "tu catálogo (personal y global)",
            "local" => "tu catálogo personal",
            "global" => "nuestro catálogo global",
            "edamam" => "Edamam",
            "internet" => "internet",
            _ => source,
        };
        // Tracks whether EVERY matched item came from this user's own personal catalog, so we
        // can skip offering to save it there again - it's already saved.
        var allFromPersonalCatalog = source == "local";

        switch (source)
        {
            // Combined "Catálogo" button: personal (already-confirmed by this user) takes
            // priority per food, falling back to the shared global catalog for that same food.
            case "catalog":
            {
                var dbFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
                var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
                int? personId = dbFactory is null || personProvider is null
                    ? null
                    : await personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);

                allFromPersonalCatalog = true;
                for (var i = 0; i < choice.Queries.Length; i++)
                {
                    var catalogQuery = i < choice.OriginalQueries.Length && !string.IsNullOrWhiteSpace(choice.OriginalQueries[i])
                        ? choice.OriginalQueries[i]
                        : choice.Queries[i];
                    var personalMatch = dbFactory is null || personId is null
                        ? null
                        : await TryGetPersonalCatalogNutritionAsync(dbFactory, personId.Value, catalogQuery, cancellationToken);
                    var catalogMatch = personalMatch ?? (dbFactory is null ? null : await TryGetCatalogNutritionAsync(dbFactory, catalogQuery, cancellationToken));
                    if (catalogMatch is not null)
                    {
                        nutritionItems.Add(catalogMatch);
                        if (personalMatch is null)
                        {
                            allFromPersonalCatalog = false;
                        }
                    }
                }
                break;
            }
            case "local":
            {
                var dbFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
                var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
                int? personId = dbFactory is null || personProvider is null
                    ? null
                    : await personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);

                for (var i = 0; i < choice.Queries.Length; i++)
                {
                    var catalogQuery = i < choice.OriginalQueries.Length && !string.IsNullOrWhiteSpace(choice.OriginalQueries[i])
                        ? choice.OriginalQueries[i]
                        : choice.Queries[i];
                    var personalMatch = dbFactory is null || personId is null
                        ? null
                        : await TryGetPersonalCatalogNutritionAsync(dbFactory, personId.Value, catalogQuery, cancellationToken);
                    if (personalMatch is not null)
                    {
                        nutritionItems.Add(personalMatch);
                    }
                }
                break;
            }
            case "global":
            {
                var dbFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();
                for (var i = 0; i < choice.Queries.Length; i++)
                {
                    var catalogQuery = i < choice.OriginalQueries.Length && !string.IsNullOrWhiteSpace(choice.OriginalQueries[i])
                        ? choice.OriginalQueries[i]
                        : choice.Queries[i];
                    var catalogMatch = dbFactory is null ? null : await TryGetCatalogNutritionAsync(dbFactory, catalogQuery, cancellationToken);
                    if (catalogMatch is not null)
                    {
                        nutritionItems.Add(catalogMatch);
                    }
                }
                break;
            }
            case "edamam":
            {
                if (!_edamamFoodSearchProvider.IsConfigured)
                {
                    return ("Edamam no está configurado en este momento.", null);
                }

                var edamamJson = await _edamamFoodSearchProvider.SearchFoodsNutritionJsonAsync(choice.Queries, cancellationToken);
                if (edamamJson is not null && JsonNode.Parse(edamamJson) is JsonArray edamamArray)
                {
                    foreach (var node in edamamArray)
                    {
                        if (node is JsonObject obj && obj.ContainsKey("calories"))
                        {
                            nutritionItems.Add(obj.DeepClone());
                        }
                    }
                }
                break;
            }
            case "internet":
            {
                if (!_bingFoodSearchProvider.IsConfigured)
                {
                    return ("La búsqueda en internet no está configurada en este momento.", null);
                }

                var bingJson = choice.Queries.Length == 1
                    ? await _bingFoodSearchProvider.SearchFoodNutritionJsonAsync(choice.Queries[0], cancellationToken)
                    : await _bingFoodSearchProvider.SearchFoodsNutritionJsonAsync(choice.Queries, cancellationToken);
                if (bingJson is not null && JsonNode.Parse(bingJson) is { } bingParsed)
                {
                    if (bingParsed is JsonArray bingArray)
                    {
                        foreach (var node in bingArray)
                        {
                            if (node is JsonObject obj && obj.ContainsKey("calories"))
                            {
                                nutritionItems.Add(obj.DeepClone());
                            }
                        }
                    }
                    else if (bingParsed is JsonObject singleObj && singleObj.ContainsKey("calories"))
                    {
                        nutritionItems.Add(singleObj.DeepClone());
                    }
                }
                break;
            }
            default:
                return ("Fuente de búsqueda desconocida.", null);
        }

        if (nutritionItems.Count == 0)
        {
            return ($"No encontré resultados en {sourceLabel} para \"{string.Join(", ", choice.OriginalQueries)}\". " +
                "¿Quieres intentar con otra fuente?", null);
        }

        ComposeMealResult? composed;
        try
        {
            var nowLocal = TimeZoneInfo.ConvertTime(DateTime.UtcNow, MealTimeHelper.Central);
            var composeResponse = await _chatClient!.CompleteChatAsync(
                [
                    new SystemChatMessage(
                        "Con estos datos nutricionales en JSON (uno por alimento, cada uno con su 'source'), " +
                        "calcula los TOTALES sumando todos los alimentos y redacta una respuesta breve en " +
                        "español (campo 'replyText') citando la fuente específica de cada alimento. Si " +
                        "'alreadyConsumed' es true, usa tono de pasado y SIEMPRE pregunta explícitamente si " +
                        "quiere agregarlo a su registro de hoy; si es false, responde la pregunta informativa " +
                        "y de todas formas pregunta si quiere agregarlo. Responde SOLO con JSON: " +
                        "{\"replyText\": string, \"mealType\": \"breakfast\"|\"lunch\"|\"dinner\"|\"snack\", " +
                        "\"description\": string, \"servingSize\": string|null, \"calories\": number|null, " +
                        "\"proteinGrams\": number|null, \"carbsGrams\": number|null, \"fatGrams\": number|null, " +
                        "\"saturatedFatGrams\": number|null, \"sugarGrams\": number|null, \"fiberGrams\": " +
                        "number|null, \"sodiumMilligrams\": number|null, \"potassiumMilligrams\": number|null, " +
                        "\"calciumMilligrams\": number|null, \"ironMilligrams\": number|null, " +
                        "\"magnesiumMilligrams\": number|null, \"vitaminAMicrograms\": number|null, " +
                        "\"sourceBreakdown\": string} - todos los totales son la SUMA de todos los alimentos."),
                    new UserChatMessage(
                        $"[Fecha y hora actual: {nowLocal:yyyy-MM-dd HH:mm} ({nowLocal:dddd})]\n" +
                        $"[alreadyConsumed: {choice.AlreadyConsumed}]\n[mealType sugerido: {choice.MealType}]\n" +
                        $"[Fuente elegida por el usuario: {sourceLabel}]\n\n" +
                        $"Pregunta original del usuario: {choice.OriginalPrompt}\n\nDatos nutricionales JSON: {nutritionItems.ToJsonString()}"),
                ],
                new ChatCompletionOptions { ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonObjectFormat() },
                cancellationToken);
            composed = JsonSerializer.Deserialize<ComposeMealResult>(composeResponse.Value.Content[0].Text, FastPathJsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ("Encontré datos pero no pude redactar la respuesta. Intenta de nuevo.", null);
        }

        if (composed is null || string.IsNullOrWhiteSpace(composed.ReplyText))
        {
            return ("Encontré datos pero no pude redactar la respuesta. Intenta de nuevo.", null);
        }

        var pendingMeal = new PendingMealDto(
            composed.MealType ?? choice.MealType,
            composed.Description ?? (choice.OriginalQueries.Length > 0 ? choice.OriginalQueries[0] : choice.Queries[0]),
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
            composed.SourceBreakdown,
            AlreadyInPersonalCatalog: allFromPersonalCatalog && nutritionItems.Count > 0);

        return (composed.ReplyText, pendingMeal);
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
