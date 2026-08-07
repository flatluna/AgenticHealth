using System.ClientModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using PersonalAgent.Common;
using PersonalAgent.Data;

namespace PersonalAgent.Agents;

/// <summary>
/// Routes a user request to the correct specialized agent (Diet, Exercise, or general
/// Personal). Implemented as its own AIAgent with tool-calling: each specialized agent
/// is exposed as a plain AIFunction tool (same pattern HumanOS uses for tools -
/// AIFunctionFactory.Create - no MCP/Harness needed for in-process routing), and the
/// orchestrator's instructions tell it to always call exactly one of them and return
/// that tool's result verbatim.
/// </summary>
public sealed class OrchestratorAgent
{
    private const string Instructions = """
        Eres un orquestador que enruta la pregunta del usuario al especialista correcto.

        Reglas:
        - Si la pregunta trata sobre dieta, nutrición, alimentos o conteo de calorías,
          llama a la herramienta "ask_diet_agent".
        - Si la pregunta trata sobre ejercicio, entrenamiento o actividad física,
          llama a la herramienta "ask_exercise_agent".
        - Si la pregunta trata sobre el HISTORIAL REAL ya registrado del usuario (ej. "¿qué
          comí hoy?", "¿cuánto ejercicio hice esta semana?", "¿cómo va mi peso?", "¿cómo voy
          con mi meta?", "¿cuál es mi estatura/nivel de actividad guardado?"), llama a la
          herramienta "ask_advisor_agent" en vez de las de dieta/ejercicio - esas dan
          consejo general, esta reporta datos reales guardados (incluye el perfil: nombre,
          estatura, peso actual, nivel de actividad).
        - EXCEPCIÓN de velocidad: cada mensaje del usuario puede venir precedido de un bloque
          "[Datos actuales ya guardados del usuario]" con su perfil (peso actual, estatura,
          nivel de actividad), lo registrado HOY (comidas, ejercicio, metas activas) y su
          peso de los últimos 8 días. Si esos datos YA incluidos bastan para responder la
          pregunta (ej. "¿cuál es mi peso?", "¿qué he comido hoy?", "¿hice ejercicio hoy?",
          "¿cómo va mi peso esta semana?", "¿cuál es mi estatura/nivel de actividad?"),
          respóndela TÚ MISMO directamente con ese texto, en español, sin llamar a NINGUNA
          herramienta - es mucho más rápido que consultar al especialista. Solo llama a
          "ask_advisor_agent" cuando la pregunta pida historial más allá de esa ventana
          (hace más de una semana, un mes, etc.) o detalles de un plan de metas que no
          vienen en ese bloque.
        - EXCEPCIÓN importante: si el usuario quiere REGISTRAR/AGREGAR una comida de HOY
          refiriéndose a una comida pasada en vez de describirla de nuevo (ej. "hoy quiero
          lo mismo que ayer", "agrégame los mismos huevos con chorizo de la semana pasada"),
          NO uses "ask_advisor_agent" - llama a "ask_diet_agent", que sí puede consultar el
          historial reciente Y registrar la comida nueva. Usa "ask_advisor_agent" solo para
          preguntas de solo lectura, no cuando la intención es registrar algo nuevo.
        - En cualquier otro caso (preguntas personales generales), llama a la herramienta
          "ask_personal_agent".
        - IMPORTANTE - continuidad de conversación: si el mensaje del usuario es una
          respuesta corta de confirmación o seguimiento (ej. "sí", "no", "dale", "claro",
          "agrégalo", "confirmo", "cámbialo", o cualquier respuesta breve sin tema explícito)
          que continúa un intercambio anterior en ESTA MISMA conversación, NO decidas el
          tema desde cero: enruta a la MISMA herramienta que usaste en tu turno anterior
          (revisa el historial de esta conversación), para que el especialista correcto
          (el que tiene el contexto pendiente, ej. una comida esperando confirmación) reciba
          el mensaje. Solo cambia de herramienta si el usuario claramente cambia de tema.
        - Llama exactamente UNA herramienta por petición del usuario.
        - La herramienta te devuelve el texto de respuesta del especialista. Responde al
          usuario con ESE MISMO texto tal cual, como un mensaje de chat normal en texto
          plano: sin comillas envolventes, sin escapes de JSON (nada de \n literal, usa
          saltos de línea reales), sin reformatearlo ni resumirlo. Simplemente entrega el
          contenido como si tú mismo lo hubieras escrito.
        """;

    private readonly ChatClient? _chatClient;
    private readonly DietAgent _dietAgent;
    private readonly ExerciseAgent _exerciseAgent;
    private readonly PersonalGeneralAgent _personalGeneralAgent;
    private readonly AdvisorAgent _advisorAgent;
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private static readonly TimeZoneInfo CentralTimeZone = ResolveCentralTimeZone();

    public OrchestratorAgent(
        IConfiguration configuration,
        DietAgent dietAgent,
        ExerciseAgent exerciseAgent,
        PersonalGeneralAgent personalGeneralAgent,
        AdvisorAgent advisorAgent,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
        _dietAgent = dietAgent;
        _exerciseAgent = exerciseAgent;
        _personalGeneralAgent = personalGeneralAgent;
        _advisorAgent = advisorAgent;
        _dbContextFactory = dbContextFactory;
        _personProvider = personProvider;

        var endpoint = configuration["AzureOpenAIEndpoint"];
        var deploymentName = configuration["AzureOpenAIDeploymentName"];
        var apiKey = configuration["AzureOpenAIApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deploymentName))
        {
            _chatClient = null;
            return;
        }

        AzureOpenAIClient client = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));

        _chatClient = client.GetChatClient(deploymentName);
    }

    public bool IsConfigured => _chatClient is not null;

    public async Task<string> AskAsync(string prompt, string sessionId, string? azureObjectId, string? userName = null, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("OrchestratorAgent is not configured (missing Azure OpenAI settings).");
        }

        IList<AITool> tools =
        [
            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario sobre dieta, nutrición o calorías.")] string userMessage) =>
                    _dietAgent.AskAsync(userMessage, sessionId, azureObjectId, userName, cancellationToken),
                "ask_diet_agent",
                "Reenvía la pregunta al especialista en dieta, nutrición y conteo de calorías."),

            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario sobre ejercicio o entrenamiento.")] string userMessage) =>
                    _exerciseAgent.AskAsync(userMessage, sessionId, azureObjectId, userName, cancellationToken),
                "ask_exercise_agent",
                "Reenvía la pregunta al especialista en ejercicio y entrenamiento."),

            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario, de tipo personal/general.")] string userMessage) =>
                    _personalGeneralAgent.AskAsync(userMessage, userName, cancellationToken),
                "ask_personal_agent",
                "Reenvía la pregunta al asistente personal general (catch-all)."),

            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario sobre su historial real: comidas, ejercicio, peso, metas o perfil ya registrados.")] string userMessage) =>
                    _advisorAgent.AskAsync(userMessage, azureObjectId, userName, cancellationToken),
                "ask_advisor_agent",
                "Reenvía la pregunta al asesor que consulta el historial REAL guardado del usuario (comidas, ejercicio, peso, metas, perfil)."),
        ];

        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: Instructions, name: "OrchestratorAgent", tools: tools);
        var session = await GetOrCreateSessionAsync(agent, sessionId, cancellationToken);

        var quickContext = await BuildQuickContextAsync(azureObjectId, cancellationToken);
        var fullPrompt = string.IsNullOrEmpty(quickContext) ? prompt : $"{quickContext}\nPregunta del usuario: {prompt}";

        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);
        return response.Text;
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

    /// <summary>
    /// Cheap, deterministic pre-fetch of the profile + today's activity (a handful of small
    /// indexed queries, no LLM involved) so the orchestrator can answer common questions
    /// ("¿cuál es mi peso?", "¿qué comí hoy?") directly in this single LLM call instead of
    /// paying for a nested ask_advisor_agent round-trip (its own tool-decision + compose
    /// calls) every time. Best-effort: any failure just returns an empty string, leaving
    /// ask_advisor_agent as the fallback path.
    /// </summary>
    private async Task<string> BuildQuickContextAsync(string? azureObjectId, CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return string.Empty;
        }

        try
        {
            var personId = await _personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);
            await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(DateTime.UtcNow, CentralTimeZone).Date, DateTimeKind.Unspecified),
                CentralTimeZone);

            var person = await db.People
                .Where(p => p.Id == personId)
                .Select(p => new { p.HeightCm, p.CurrentWeightKg, p.ActivityLevel })
                .FirstOrDefaultAsync(cancellationToken);

            var todayMeals = await db.MealLogs
                .Where(m => m.PersonId == personId && m.RecordedAtUtc >= todayStartUtc)
                .Select(m => new { m.MealType, m.Description, m.Calories })
                .ToListAsync(cancellationToken);

            var todayExercise = await db.ExerciseLogs
                .Where(e => e.PersonId == personId && e.RecordedAtUtc >= todayStartUtc)
                .Select(e => new { e.Description, e.DurationMinutes, e.CaloriesBurned })
                .ToListAsync(cancellationToken);

            var activeGoals = await db.Goals
                .Where(g => g.PersonId == personId && g.Status == GoalStatus.Active)
                .Select(g => new { g.Type, g.TargetValue })
                .ToListAsync(cancellationToken);

            var recentWeights = await db.WeightLogs
                .Where(w => w.PersonId == personId && w.RecordedAtUtc >= todayStartUtc.AddDays(-8))
                .OrderBy(w => w.RecordedAtUtc)
                .Select(w => new { w.WeightKg, w.RecordedAtUtc })
                .ToListAsync(cancellationToken);

            var heightText = person?.HeightCm is { } h ? $"{h} cm" : "no guardada";
            var weightText = person?.CurrentWeightKg is { } w ? $"{w} kg" : "no guardado";
            var activityText = person?.ActivityLevel is { } activity ? activity.ToString() : "no guardado";

            var sb = new StringBuilder();
            sb.AppendLine("[Datos actuales ya guardados del usuario]");
            sb.AppendLine($"- Perfil: estatura {heightText}, peso actual {weightText}, nivel de actividad {activityText}.");
            sb.AppendLine(todayMeals.Count == 0
                ? "- Comidas de hoy: ninguna registrada."
                : $"- Comidas de hoy ({todayMeals.Sum(m => m.Calories ?? 0):0} kcal total): " +
                  string.Join("; ", todayMeals.Select(m => $"{m.MealType}: {m.Description} ({m.Calories} kcal)")));
            sb.AppendLine(todayExercise.Count == 0
                ? "- Ejercicio de hoy: ninguno registrado."
                : "- Ejercicio de hoy: " +
                  string.Join("; ", todayExercise.Select(e => $"{e.Description} ({e.DurationMinutes} min, {e.CaloriesBurned} kcal quemadas)")));
            sb.AppendLine(activeGoals.Count == 0
                ? "- Metas activas: ninguna."
                : "- Metas activas: " + string.Join("; ", activeGoals.Select(g => $"{g.Type}: {g.TargetValue}")));
            sb.AppendLine(recentWeights.Count == 0
                ? "- Peso últimos 8 días: sin registros."
                : "- Peso últimos 8 días: " + string.Join("; ", recentWeights.Select(w =>
                    $"{TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(w.RecordedAtUtc, DateTimeKind.Utc), CentralTimeZone):yyyy-MM-dd}: {w.WeightKg} kg")));

            return sb.ToString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static TimeZoneInfo ResolveCentralTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        }
    }
}
