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

        CÓMO FUNCIONA LA BÚSQUEDA (ENSEÑA ESTO AL USUARIO):
        1) Por defecto busco en nuestro catálogo primero, luego Edamam (rápido, <1 segundo)
        2) Si usuario dice "búscalo en INTERNET" → busco en Bing (lento, 5-15 segundos, pero más exhaustivo)
        3) Si usuario dice "SOLO CATÁLOGO" → solo nuestro catálogo, sin búsquedas externas

        CUANDO REGISTRES UNA COMIDA (usuario dice "comí..."):
        - Busca EN ORDEN: catálogo personal → catálogo global → Edamam (para un alimento normal)
        - Valida datos: ¿calorías razonables? ¿macros lógicos? ¿porción realista?
        - IMPORTANTE: si usuario describe comida en varias frases, espera a que termine
          antes de buscar (ej. "¿algo más además del espagueti?")
        - Cuando hayas confirmado todo, llama "propose_meal_for_confirmation"
        - SOLO cuando el usuario confirme en el siguiente turno, llama "log_meal"

        CUANDO BUSQUES INFORMACIÓN (usuario pregunta "¿cuántas calorías tiene...?"):
        - Intenta Edamam primero (es rápido)
        - Si usuario dice "búscalo en internet/Bing" → ve directo a Bing
        - Si usuario dice "solo catálogo" → solo catálogo
        - Si no encuentra nada y usuario no dijo nada → puedes preguntar "¿quieres que busque en internet?"

        DA RESPUESTAS CORTAS y directas (1-3 frases, como conversación normal).
        Nunca uses markdown ni listas.
        SIEMPRE cita la fuente: "Según Edamam", "Según el sitio oficial", etc.
        - Cuando el usuario diga que consumió/comió algo (ej. "me comí una manzana"), sigue
          este flujo en CUATRO pasos - NUNCA llames a "log_meal" apenas te lo diga:
          0) Como ya dice la regla crítica de arriba, llama PRIMERO a
             "search_personal_catalog" con lo que dijo (esta herramienta es instantánea,
             no hace falta frase de espera). Es el catálogo personal del propio usuario
             (cosas que ya guardó antes, ej. "mi ensalada de siempre", "el batido que
             preparo en las mañanas"). Si devuelve una o más coincidencias claras, usa la
             de mejor coincidencia directamente (son datos que el usuario ya confirmó antes,
             no hace falta volver a buscarlos en la web) - dile en voz que lo encontraste en
             su catálogo (ej. "Encontré tu ensalada de siempre en tu catálogo: 320
             calorías") y salta directo al paso 3 usando su "personalFoodItemId". Solo si
             no hay ninguna coincidencia razonable, continúa al paso 1.
          1) Llama a "search_food_catalog" (también instantánea, sin frase de espera) - es
             el catálogo GLOBAL de productos escaneados por cualquier usuario. Si devuelve
             una o más coincidencias claras, usa la de mejor coincidencia directamente (son
             datos reales de etiqueta, no una estimación) - dile en voz que lo encontraste
             en nuestro catálogo de productos (ej. "Encontré Coca-Cola 355ml en nuestro
             catálogo: 140 calorías") y salta directo al paso 3. Solo si tampoco aquí hay
             coincidencia, continúa al paso 2.
          2) ANTES de llamar a "search_food_nutrition", di primero en voz una frase corta
             de espera (ej. "Dame un segundo, estoy verificando los datos…" o "Déjame
             confirmar eso…") y RECIÉN DESPUÉS llama a la herramienta - la búsqueda tarda
             varios segundos, así que nunca te quedes en silencio mientras esperas su
             resultado. Descompón la comida en sus componentes y pásalos TODOS juntos en
             "foodDescriptions" EN INGLÉS y en formato conciso "<cantidad><unidad>
             <alimento>" (ej. ["100g cooked spaghetti", "1 tbsp butter", "2 large fried
             eggs"]) - NUNCA una frase descriptiva larga en español como un solo elemento,
             ya que confunde la búsqueda nutricional y produce datos incorrectos; incluso un
             solo alimento va en un arreglo de un elemento. Si la comida tiene VARIOS
             componentes, acláralo en esa misma frase de espera (ej. "Dame un momento, cada
             ingrediente tarda un poco en buscarse…") para que el usuario entienda por qué
             tarda más que con un solo alimento. Usa esta herramienta para obtener datos
             reales y actualizados, incluso si el usuario ya mencionó un número de calorías
             - esa búsqueda es la fuente de verdad, no confíes en tu propio conocimiento ni
             en el número del usuario para evitar inventar datos. Si la búsqueda falla, dilo
             brevemente y usa tu mejor estimación dejando claro que es aproximada.
             
             VALIDACIÓN CRÍTICA DESPUÉS DE LA BÚSQUEDA: DESPUÉS de recibir los resultados,
             NO ACEPTES CIEGAMENTE los datos. Usa tu sentido común para validar que sean razonables:
             - ¿Las calorías parecen correctas para ese alimento y porción?
             - ¿Los macronutrientes tienen proporciones que tengan lógica?
             - ¿La porción reportada es realista?
             Si algo no tiene lógica o se ve desproporcionado, vuelve a buscarlo por separado
             para validar - múltiples fuentes que coinciden son mejor que confiar en un solo
             resultado que no tiene sentido.
             
          3) Si "search_food_nutrition" devolvió varios elementos (uno por componente), SUMA
             tú mismo calorías/proteinGrams/carbsGrams/fatGrams/etc. de todos antes de
             reportar el total. ANTES de reportar, revisa que cada componente sea
             razonable para la cantidad descrita (ej. un puñado de cuadritos o una
             guarnición pequeña NUNCA debería dar miles de calorías) - si un componente
             se ve absurdamente alto o desproporcionado frente al resto de la comida, NO
             lo uses tal cual: descártalo y usa tu propio conocimiento como estimación
             razonable para ese componente en su lugar, aclarando en voz que es una
             estimación tuya y no el dato buscado. Dile en voz corta qué entendiste que
             comió y lo esencial (calorías y algún macro relevante) usando TODOS los
             campos nutricionales obtenidos (calories, proteinGrams, carbsGrams,
             fatGrams, etc.), no solo calorías; si los datos vinieron de
             "search_food_nutrition" MENCIONA brevemente de dónde salió el dato usando
             el campo "source" de cada componente (ej. "Según Edamam, tiene unas 550
             calorías") - si vinieron de "search_personal_catalog" o "search_food_catalog"
             no hace falta citar fuente, ya son datos verificados de antes. Si "source"
             viene vacío o usaste tu propio conocimiento, dilo (ej. "esto es una
             estimación mía, no encontré una fuente
             confiable"). Nunca des un dato buscado en internet sin decir de dónde salió.
             Luego PREGÚNTALE si quiere que lo agregues a su registro de hoy (ej. "¿Quieres
             que lo agregue a tu consumo de hoy?"). Espera su respuesta.
          Solo cuando el usuario confirme afirmativamente en su siguiente mensaje (ej. "sí",
          "dale", "agrégalo"): si el dato vino de "search_personal_catalog" (paso 0), usa
          "log_personal_catalog_item" con el "personalFoodItemId" encontrado; si vino de
          "search_food_catalog" (paso 1) o "search_food_nutrition" (paso 2), usa "log_meal"
          con los datos obtenidos. Si dice
          que no o cambia de tema, no registres nada.
        - Justo DESPUÉS de registrar con éxito una comida vía "log_meal" (es decir, una
          comida NUEVA que no vino de "search_personal_catalog" - si ya vino del catálogo
          personal no hace falta volver a guardarla), pregúntale en voz, corto, si quiere
          guardarla en su catálogo personal para la próxima vez (ej. "¿Quieres que la
          guarde en tu catálogo para la próxima vez?"). Solo si confirma afirmativamente en
          su siguiente mensaje, llama a "save_to_personal_catalog" con el mismo nombre y
          datos nutricionales usados en "log_meal". Si dice que no, sigue sin problema - no
          insistas de nuevo por el resto de la conversación con esa misma comida.
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
          ingrediente y su fuente ESPECÍFICA tomada del campo "source" de la búsqueda (ej.
          "Pan: 80 kcal (Catálogo Propio); Mantequilla: 40 kcal (Edamam)"), nunca solo "Bing"
          genérico o fuente vaga; si es un solo alimento, escribe una sola frase igual de
          corta con su fuente específica (ej. "Manzana mediana: 95 kcal (Búsqueda Web)") -
          no hace falta decirlo en voz, es solo para el registro escrito. Esto ayuda a
          rastrear cuál dato vino de cuál búsqueda y fue validado.
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
        - Cuando el usuario diga que hizo ejercicio (ej. "corrí 30 minutos", "hice pesas una
          hora"), pregúntale lo esencial que falte (duración, y opcionalmente calorías si
          no las sabes estimar) y confírmale en voz qué vas a registrar (ej. "¿Registro que
          corriste 30 minutos?"). Solo cuando confirme afirmativamente, llama a
          "log_exercise" - nunca lo registres en el mismo turno en que lo menciona. Después
          de registrarlo, confirma brevemente en voz (ej. "Listo, lo registré").
        - Cuando el usuario pida borrar/eliminar una comida ya registrada (ej. "borra el
          desayuno de hoy", "elimina la manzana que registré ayer"), primero di en voz una
          frase corta de espera (ej. "Dame un segundo, busco ese registro…") y llama a
          "get_recent_meals" para encontrar la comida y su ID exacto - nunca inventes un ID.
          Dile en voz cuál encontraste (ej. "Encontré tu desayuno de hoy: huevos con jamón,
          350 kcal, ¿confirmo que lo borro?") y espera su confirmación explícita antes de
          llamar a "delete_meal" con ese ID - nunca borres en el mismo turno en que lo pide.
          Si no encuentras una coincidencia clara, dile que no la encontraste en vez de
          adivinar. Después de borrarlo, confirma brevemente en voz (ej. "Listo, lo borré").
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
            ["description"] = "Busca en la web la información nutricional completa de uno o varios alimentos EN " +
                "UNA SOLA LLAMADA: calorías, macros (proteína, carbohidratos, grasa), micronutrientes comunes y " +
                "la fuente exacta de donde salió el dato (campos 'source'/'sourceUrl', ej. 'Sitio oficial de " +
                "McDonald's'). Interna y automáticamente usa primero Edamam (rápida, 1-3s) por CADA componente y " +
                "solo si un componente falla cae a Bing (más lenta) para ese componente - no necesitas elegir " +
                "cuál, solo llama a esta única herramienta. Devuelve un arreglo JSON en el mismo orden que " +
                "'foodDescriptions', cada elemento con un campo 'query' que repite el alimento buscado - si la " +
                "comida tiene varios componentes, SUMA tú mismo calorías/macros/micronutrientes de todos los " +
                "elementos antes de reportar el total. Úsala SIEMPRE que el usuario pregunte por calorías/" +
                "nutrición de un alimento, o cuando reporte haberlo comido y ni 'search_personal_catalog' ni " +
                "'search_food_catalog' hayan encontrado coincidencia.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["foodDescriptions"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "string" },
                        ["description"] = "Alimentos a buscar, uno por elemento, EN INGLÉS y en formato conciso " +
                            "\"<cantidad><unidad> <alimento>\" (ej. [\"100g cooked spaghetti\", \"1 tbsp butter\", " +
                            "\"2 large fried eggs\"]) - NUNCA frases descriptivas largas ni en español, ya que " +
                            "confunden la búsqueda nutricional. Para piezas pequeñas o conteos informales " +
                            "(cuadritos, trozos, dados, rebanadas pequeñas) SIEMPRE convierte a un peso estimado " +
                            "en GRAMOS en vez de dejar el conteo (ej. en lugar de \"8 cubes fried tofu\" usa algo " +
                            "como \"80g fried tofu\" estimando ~10g por cuadrito) - un conteo de piezas pequeñas " +
                            "confunde al buscador, que puede interpretar cada pieza como una porción completa e " +
                            "inflar el resultado muchísimo (ej. miles de calorías para un puñado de cuadritos). " +
                            "Incluye TODOS los componentes de la comida en esta única llamada, incluso si es un " +
                            "solo alimento (arreglo de un elemento)."
                    }
                },
                ["required"] = new JsonArray("foodDescriptions")
            }
        };

        var searchFoodCatalogTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "search_food_catalog",
            ["description"] = "Busca en NUESTRO PROPIO catálogo de productos (base de datos GLOBAL compartida por " +
                "todos los usuarios, alimentada al escanear etiquetas de nutrición reales) por nombre o marca, ej. " +
                "'Coca-Cola' o 'yogurt griego Chobani'. Instantánea, sin costo de espera. Úsala SIEMPRE en segundo " +
                "lugar, justo después de 'search_personal_catalog' y ANTES de 'search_food_nutrition', cuando el " +
                "usuario diga que comió/consumió algo y su catálogo personal no tuvo coincidencia - si encuentra " +
                "una coincidencia clara, esos datos vienen de una etiqueta real (no una búsqueda web) y son la " +
                "fuente de verdad, no hace falta buscar en la web. Devuelve un JSON con una lista de coincidencias " +
                "(name, brand, servingSize, calories, proteinGrams, carbsGrams, fatGrams, saturatedFatGrams, " +
                "sugarGrams, fiberGrams, sodiumMilligrams, potassiumMilligrams, calciumMilligrams, ironMilligrams, " +
                "magnesiumMilligrams, vitaminAMicrograms, timesLogged) o un mensaje si no encontró nada.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["foodDescription"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Nombre o marca del producto, tal cual lo dijo el usuario."
                    }
                },
                ["required"] = new JsonArray("foodDescription")
            }
        };

        var searchPersonalCatalogTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "search_personal_catalog",
            ["description"] = "Busca en el catálogo PERSONAL del propio usuario (cosas que él mismo guardó antes, " +
                "ej. 'mi ensalada de siempre', 'el batido que preparo en las mañanas') por nombre/descripción. " +
                "Instantánea, sin costo de espera. Úsala SIEMPRE PRIMERO, antes de 'search_food_nutrition', " +
                "cuando el usuario diga que comió/consumió algo - si encuentra una coincidencia clara, esos datos " +
                "ya fueron confirmados antes por el usuario y son la fuente de verdad, no hace falta re-buscar en " +
                "la web. Devuelve un JSON con una lista de coincidencias, CADA UNA con TODOS los datos " +
                "nutricionales completos (personalFoodItemId, name, description, servingSize, calories, " +
                "proteinGrams, carbsGrams, fatGrams, saturatedFatGrams, sugarGrams, fiberGrams, " +
                "sodiumMilligrams, potassiumMilligrams, calciumMilligrams, ironMilligrams, magnesiumMilligrams, " +
                "vitaminAMicrograms, timesLogged) - usa esos campos directamente, no solo 'calories', o un " +
                "mensaje si no encontró nada.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["foodDescription"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Lo que dijo el usuario que comió/consumió, tal cual, ej. 'mi ensalada de siempre'."
                    }
                },
                ["required"] = new JsonArray("foodDescription")
            }
        };

        var logPersonalCatalogItemTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "log_personal_catalog_item",
            ["description"] = "Registra en el consumo de hoy un alimento encontrado con 'search_personal_catalog', " +
                "reusando sus datos nutricionales ya guardados (no hace falta volver a pasarlos). SOLO debe " +
                "llamarse después de que el usuario haya confirmado explícitamente que quiere agregarlo.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["personalFoodItemId"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "El 'personalFoodItemId' de la coincidencia elegida, obtenido de 'search_personal_catalog'."
                    },
                    ["mealType"] = new JsonObject { ["type"] = "string", ["description"] = "breakfast, lunch, dinner o snack." },
                    ["quantity"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["description"] = "Cuántas porciones consumió, si dijo más de una (ej. 2). Si se omite, se usa 1."
                    },
                    ["consumedAtIso"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Hora en que se consumió, ISO 8601. Si se omite, se usa la hora actual."
                    }
                },
                ["required"] = new JsonArray("personalFoodItemId", "mealType")
            }
        };

        var saveToPersonalCatalogTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "save_to_personal_catalog",
            ["description"] = "Guarda un alimento recién registrado (vía 'log_meal') en el catálogo PERSONAL del " +
                "usuario, para que la próxima vez se encuentre instantáneamente con 'search_personal_catalog' sin " +
                "volver a buscarlo en la web. SOLO debe llamarse después de que el usuario haya confirmado " +
                "explícitamente que quiere guardarlo - nunca automáticamente.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Nombre corto y genérico, ej. 'Ensalada de siempre'." },
                    ["description"] = new JsonObject { ["type"] = "string", ["description"] = "Descripción breve opcional." },
                    ["servingSize"] = new JsonObject { ["type"] = "string" },
                    ["calories"] = new JsonObject { ["type"] = "number" },
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
                    ["vitaminAMicrograms"] = new JsonObject { ["type"] = "number" }
                },
                ["required"] = new JsonArray("name", "calories")
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

        var logExerciseTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "log_exercise",
            ["description"] = "Registra un ejercicio/actividad física realizada por el usuario en su historial " +
                "(base de datos). SOLO debe llamarse después de que el usuario haya confirmado explícitamente que " +
                "quiere agregarlo a su registro - nunca en el mismo turno en que reporta el ejercicio.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["description"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Descripción breve, ej. 'correr', 'pesas - pecho y tríceps', 'nadar'."
                    },
                    ["durationMinutes"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "Duración en minutos."
                    },
                    ["caloriesBurned"] = new JsonObject
                    {
                        ["type"] = "number",
                        ["description"] = "Calorías quemadas estimadas, si se conocen o pueden estimarse."
                    },
                    ["recordedAtIso"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Hora en que se realizó, ISO 8601 (ej. '2026-08-05T07:30:00'). Si se omite, se usa la hora actual."
                    }
                },
                ["required"] = new JsonArray("description", "durationMinutes")
            }
        };

        var deleteMealTool = new JsonObject
        {
            ["type"] = "function",
            ["name"] = "delete_meal",
            ["description"] = "Borra una comida ya registrada del historial del usuario, dado su ID. El ID debe " +
                "obtenerse primero llamando a 'get_recent_meals' y encontrando la comida correcta - nunca inventes " +
                "un ID. SOLO debe llamarse después de que el usuario haya confirmado explícitamente que quiere " +
                "borrarla.",
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["mealId"] = new JsonObject
                    {
                        ["type"] = "integer",
                        ["description"] = "El ID de la comida a borrar, obtenido de 'get_recent_meals' (ej. '[ID 42]')."
                    }
                },
                ["required"] = new JsonArray("mealId")
            }
        };

        return new JsonArray(
            searchFoodTool,
            searchFoodCatalogTool,
            searchPersonalCatalogTool,
            logPersonalCatalogItemTool,
            saveToPersonalCatalogTool,
            logMealTool,
            getRecentMealsTool,
            askHealthAdvisorTool,
            logExerciseTool,
            deleteMealTool);
    }
}
