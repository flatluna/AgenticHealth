using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using PersonalAgent.Common;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Mints a short-lived Azure OpenAI GPT Realtime ephemeral session for the "Habla con tu
/// agente" voice mode in the chat page. Unlike a plain conversational-only voice channel,
/// this session is given REAL function-calling tools ("log_meal", "search_food_nutrition")
/// so the user can register meals just by talking (per explicit product decision - this
/// app is simple enough that voice should be able to act, not just chat).
///
/// Azure's Realtime API cannot execute tools itself - when the model decides to call one,
/// it sends the call (name/arguments/call_id) to the BROWSER over the WebRTC data channel.
/// The browser then calls back into VoiceToolsFunction's REST endpoints to actually run the
/// tool (e.g. insert a MealLog row) and reports the result back over the data channel. This
/// endpoint never proxies audio, and the real Azure OpenAI API key never leaves the backend
/// (see RealtimeVoiceSessionService).
/// </summary>
public sealed class VoiceChatSessionFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string Instructions = """
        Eres el asistente personal de salud de AgenticHealth, hablando por voz en tiempo
        real con el usuario. Hablas español de forma natural, cálida y conversacional.

        Reglas importantes para voz:
        - Da respuestas CORTAS y directas, como en una conversación hablada real (1-3
          frases). Nunca uses listas con viñetas, markdown, ni texto largo tipo artículo.
        - Puedes conversar sobre dieta, nutrición, calorías, ejercicio, hábitos saludables
          y preguntas generales de bienestar.
        - Cuando el usuario diga que consumió/comió algo (ej. "me comí una manzana"), sigue
          este flujo en DOS pasos - NUNCA llames a "log_meal" apenas te lo diga:
          1) ANTES de llamar a "search_food_nutrition", di primero en voz una frase corta
             de espera (ej. "Dame un segundo, estoy verificando los datos…" o "Déjame
             confirmar eso…") y RECIÉN DESPUÉS llama a la herramienta - la búsqueda tarda
             varios segundos, así que nunca te quedes en silencio mientras esperas su
             resultado. Usa esta herramienta para obtener datos reales y actualizados de
             ese alimento, incluso si el usuario ya mencionó un número de calorías - esa
             búsqueda es la fuente de verdad, no confíes en tu propio conocimiento ni en el
             número del usuario para evitar inventar datos. Si la búsqueda falla, dilo
             brevemente y usa tu mejor estimación dejando claro que es aproximada.
          2) Dile en voz corta qué entendiste que comió y lo esencial (calorías y algún
             macro relevante), y PREGÚNTALE si quiere que lo agregues a su registro de hoy
             (ej. "¿Quieres que lo agregue a tu consumo de hoy?"). Espera su respuesta.
          Solo cuando el usuario confirme afirmativamente en su siguiente mensaje (ej. "sí",
          "dale", "agrégalo"), usa "log_meal" con los datos obtenidos en el paso 1. Si dice
          que no o cambia de tema, no registres nada.
        - No le preguntas al usuario datos nutricionales técnicos (calorías, proteína,
          etc.) - eso lo resuelves tú con las herramientas o tu conocimiento. Solo pregunta
          si falta información esencial como QUÉ comió, CUÁNTO (porción) o CUÁNDO.
        - Cuando el usuario se refiera a una comida pasada en vez de describirla de nuevo
          (ej. "lo mismo que ayer", "los mismos huevos con chorizo de la semana pasada"),
          di primero una frase corta de espera (ej. "Dame un segundo, reviso qué
          registraste…") y llama a "get_recent_meals" para ver su historial reciente ANTES
          de usar "search_food_nutrition" - no le pidas que repita la descripción. Busca en
          esa lista la comida que mejor coincida y reutiliza EXACTAMENTE esos valores
          nutricionales (incluyendo el "sourceBreakdown" guardado, agregando algo como
          "(igual que el <fecha>)"), confírmaselo en voz de forma breve (ej. "Encontré que
          ayer registraste 2 huevos con chorizo, 350 kcal, ¿registro lo mismo para hoy?") y
          espera su confirmación antes de llamar a "log_meal", igual que con una comida
          nueva. Si no encuentras una coincidencia clara, dilo y trátala como comida nueva.
        - Al llamar a "log_meal", intenta llenar también los micronutrientes (sodio, potasio,
          calcio, hierro, magnesio, vitamina A) estimándolos con tu conocimiento si la
          búsqueda no los trajo - nunca los dejes vacíos. El parámetro "sourceBreakdown" es
          OBLIGATORIO en TODA llamada a "log_meal", nunca lo omitas: si la comida tiene
          varios componentes (ej. "pan con mantequilla"), llena un desglose corto por
          ingrediente y su fuente (ej. "Pan: 80 kcal (Bing); Mantequilla: 40 kcal (Bing)");
          si es un solo alimento, escribe una sola frase igual de corta con su fuente (ej.
          "Manzana mediana: 95 kcal (Bing)") - no hace falta decirlo en voz, es solo para
          el registro escrito.
        - Después de registrar una comida (solo tras la confirmación del usuario), confirma
          brevemente en voz (ej. "Listo, la registré").
        - Cuando el usuario pregunte sobre su HISTORIAL REAL ya registrado (ej. "¿qué comí
          hoy?", "¿cuánto ejercicio hice esta semana?", "¿cómo va mi peso?", "¿cómo voy con
          mi meta?"), primero di en voz una frase corta de espera (ej. "Dame un segundo,
          reviso tu historial…") y DESPUÉS llama a la herramienta "ask_health_advisor"
          pasándole la pregunta tal cual la dijo el usuario - consultar la base de datos
          tarda unos segundos, así que nunca te quedes callado mientras esperas. Cuando
          tengas el resultado, respóndele en voz de forma breve y natural basándote en lo
          que te devolvió la herramienta - nunca inventes datos de su historial.
        - No eres un médico: para condiciones médicas serias, recomienda consultar a un
          profesional de la salud.
        - Empieza la conversación con un saludo breve y natural, preguntando en qué puedes
          ayudar hoy.
        """;

    private readonly RealtimeVoiceSessionService _voiceSessionService;
    private readonly ILogger<VoiceChatSessionFunction> _logger;

    public VoiceChatSessionFunction(RealtimeVoiceSessionService voiceSessionService, ILogger<VoiceChatSessionFunction> logger)
    {
        _voiceSessionService = voiceSessionService;
        _logger = logger;
    }

    public sealed record VoiceSessionResponse(
        string ClientSecret,
        string RealtimeCallsUrl,
        string Model,
        string Voice,
        long? ExpiresAtUnixSeconds);

    public sealed record VoiceSessionRequest(string? UserName);

    [Function("VoiceChatSession")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voice/session")]
        HttpRequestData request,
        CancellationToken cancellationToken)
    {
        if (!_voiceSessionService.IsConfigured)
        {
            return await FunctionResponseFactory.ErrorResponseAsync(
                request,
                "El modo de voz no está configurado (falta AzureOpenAIRealtimeDeploymentName en local.settings.json).",
                HttpStatusCode.ServiceUnavailable);
        }

        VoiceSessionRequest? body = null;
        try
        {
            body = await JsonSerializer.DeserializeAsync<VoiceSessionRequest>(request.Body, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // Body is optional (older clients post null) - just proceed without a user name.
        }

        try
        {
            var tools = BuildTools();
            var instructions = string.IsNullOrWhiteSpace(body?.UserName)
                ? Instructions
                : $"{Instructions}\n\nEl usuario que te habla se llama {body.UserName}. Si te pregunta cómo se " +
                  "llama o te saluda, usa su nombre directamente sin decir que no lo sabes.";
            var session = await _voiceSessionService.CreateEphemeralSessionAsync(instructions, cancellationToken, tools);

            return await FunctionResponseFactory.SuccessResponseAsync(
                request,
                new VoiceSessionResponse(session.ClientSecret, session.RealtimeCallsUrl, session.Model, session.Voice, session.ExpiresAtUnixSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VoiceChatSession failed");
            return await FunctionResponseFactory.ErrorResponseAsync(request, "No se pudo iniciar la sesión de voz.", HttpStatusCode.InternalServerError);
        }
    }

    /// <summary>
    /// Realtime function-calling tool definitions. Parameter shapes mirror DietAgent's
    /// existing "log_meal" tool 1:1 so both the text chat and voice mode write the exact
    /// same MealLog schema (see VoiceToolsFunction, which executes these on the browser's
    /// behalf).
    /// </summary>
    private static JsonArray BuildTools()
    {
        var searchFoodTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "search_food_nutrition",
            ["description"] = "Busca en la web (Bing) la información nutricional completa de un alimento: calorías, " +
                "macros (proteína, carbohidratos, grasa) y micronutrientes comunes. Devuelve un JSON.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["foodDescription"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Nombre o descripción del alimento, ej. 'una manzana mediana'."
                    }
                },
                ["required"] = new JsonArray("foodDescription")
            }
        };

        var logMealTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "log_meal",
            ["description"] = "Registra una comida consumida por el usuario en su historial (base de datos), " +
                "con toda la información nutricional disponible. SOLO debe llamarse después de haber dicho en " +
                "voz los datos nutricionales (idealmente obtenidos con search_food_nutrition) y de que el " +
                "usuario haya confirmado explícitamente que quiere agregarlo - nunca apenas reporta la comida. " +
                "El parámetro 'sourceBreakdown' es OBLIGATORIO en TODOS los casos, incluso para un solo alimento.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["mealType"] = new JsonObject { ["type"] = "string", ["description"] = "breakfast, lunch, dinner o snack." },
                    ["description"] = new JsonObject { ["type"] = "string", ["description"] = "Descripción breve, ej. 'una manzana'." },
                    ["servingSize"] = new JsonObject { ["type"] = "string", ["description"] = "Ej. '100 g' o '1 unidad mediana'." },
                    ["calories"] = new JsonObject { ["type"] = "number", ["description"] = "Calorías totales (kcal)." },
                    ["proteinGrams"] = new JsonObject { ["type"] = "number" },
                    ["carbsGrams"] = new JsonObject { ["type"] = "number" },
                    ["fatGrams"] = new JsonObject { ["type"] = "number" },
                    ["saturatedFatGrams"] = new JsonObject { ["type"] = "number" },
                    ["sugarGrams"] = new JsonObject { ["type"] = "number" },
                    ["fiberGrams"] = new JsonObject { ["type"] = "number" },
                    ["sodiumMilligrams"] = new JsonObject { ["type"] = "number" },
                    ["potassiumMilligrams"] = new JsonObject { ["type"] = "number" },
                    ["calciumMilligrams"] = new JsonObject { ["type"] = "number" },
                    ["ironMilligrams"] = new JsonObject { ["type"] = "number" },
                    ["magnesiumMilligrams"] = new JsonObject { ["type"] = "number" },
                    ["vitaminAMicrograms"] = new JsonObject { ["type"] = "number" },
                    ["consumedAtIso"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Hora en que se consumió, ISO 8601 (ej. '2026-08-03T08:30:00'). Si se omite, se usa la hora actual."
                    },
                    ["sourceBreakdown"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "OBLIGATORIO, nunca lo omitas. Desglose de cómo se calculó el total, con la " +
                            "fuente entre paréntesis. Si hay varios componentes, uno por línea o separados por ';', " +
                            "ej. 'Pan: 80 kcal (Bing); Mantequilla: 40 kcal (Bing)'. Si es un solo alimento simple, " +
                            "una sola frase corta, ej. 'Manzana mediana: 95 kcal (Bing)' o 'Estimado con conocimiento " +
                            "general, sin resultado de búsqueda'."
                    }
                },
                ["required"] = new JsonArray("mealType", "description", "calories", "sourceBreakdown")
            }
        };

        var getRecentMealsTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "get_recent_meals",
            ["description"] = "Devuelve el historial reciente de comidas YA registradas por el usuario (fecha, " +
                "descripción, porción y datos nutricionales completos), para cuando el usuario se refiera a una " +
                "comida pasada en vez de describirla de nuevo (ej. 'lo mismo que ayer').",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["daysBack"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Días hacia atrás a buscar, ej. 1 para 'ayer', 7 para 'la semana pasada'. Si se omite, usa 14."
                    }
                },
                ["required"] = new JsonArray()
            }
        };

        var askHealthAdvisorTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "ask_health_advisor",
            ["description"] = "Consulta el historial REAL guardado del usuario (comidas, ejercicio, peso, metas) " +
                "para responder preguntas sobre lo que ya comió/hizo/pesó/se propuso. Devuelve una respuesta en " +
                "texto ya redactada a partir de datos reales.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["question"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "La pregunta del usuario tal cual la formuló, ej. '¿qué comí hoy?'."
                    }
                },
                ["required"] = new JsonArray("question")
            }
        };

        return new JsonArray(searchFoodTool, logMealTool, getRecentMealsTool, askHealthAdvisorTool);
    }
}
