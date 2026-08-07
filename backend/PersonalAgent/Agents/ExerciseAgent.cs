using System.ClientModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Chat;
using PersonalAgent.Common;
using PersonalAgent.Data;
using PersonalAgent.Skills;

namespace PersonalAgent.Agents;

/// <summary>Structured result of estimating calories burned for a described activity.</summary>
public sealed class ExerciseEstimationResult
{
    /// <summary>Short, clear Spanish name for this exercise, e.g. "Caminata", "Pesas - tren superior".</summary>
    public string SuggestedName { get; set; } = string.Empty;

    /// <summary>Estimated calories burned for an average adult doing this activity for the given duration.</summary>
    public double EstimatedCaloriesBurned { get; set; }
}

/// <summary>
/// Specialized agent for exercise, training and workout-plan questions. Same self-configuring
/// pattern as DietAgent: reads Azure OpenAI settings from IConfiguration, falls back to
/// DefaultAzureCredential when no API key is set, and exposes IsConfigured so callers can fail
/// gracefully instead of crashing.
///
/// Like DietAgent, it can also log activities to PersonalAgentDB via the "log_exercise" tool -
/// since there's no MET-table/formula anywhere in the app, the LLM estimates calories burned
/// itself from its general knowledge, exactly like it already estimates food calories when no
/// catalog/Bing match exists. It keeps a per-conversation AgentSession (keyed by the
/// caller-supplied sessionId) so a "sí, agrégalo" in a later turn can complete the pending
/// exercise from an earlier turn, mirroring DietAgent's two-step confirm-before-save flow.
/// </summary>
public sealed class ExerciseAgent
{
    private const string Instructions = """
        Eres ExerciseAgent, un asistente experto en ejercicio físico, entrenamiento y
        planes de actividad.

        Reglas:
        - Responde siempre en español, de forma clara y práctica.
        - Adapta las recomendaciones al nivel, objetivo y limitaciones físicas del usuario
          cuando las conozcas; si no las conoces, pregúntalas antes de dar un plan completo.
        - No eres un fisioterapeuta ni médico: ante lesiones o dolor, recomienda consultar
          a un profesional.

        - FLUJO OBLIGATORIO cuando el usuario diga que hizo/hace ejercicio (ej. "caminé 40
          minutos", "hice pesas 1 hora", "corrí 5km"), en DOS pasos - NUNCA llames a
          "log_exercise" en el mismo turno en que el usuario reporta el ejercicio:
          1) Si no sabes la duración en minutos, pregúntala primero. Con la duración,
             estima las calorías quemadas usando tu conocimiento general de valores MET
             típicos para ese tipo de actividad e intensidad (asume un adulto promedio si
             no conoces el peso del usuario) y sugiere un nombre corto y claro para el
             ejercicio (ej. "Caminata", "Pesas - tren superior", "Trote suave"). Responde
             confirmando lo que entendiste (actividad y duración) junto con tu estimación
             de calorías, y PREGÚNTALE explícitamente si quiere que lo agregues a su
             registro de ejercicio de hoy (ej. "¿Quieres que lo agregue a tu registro de
             hoy?"). NO llames a "log_exercise" todavía.
          2) Solo cuando el usuario responda afirmativamente en un mensaje POSTERIOR
             confirmando ESE ejercicio pendiente (o pida ajustar el nombre/calorías antes
             de confirmar), usa la herramienta "log_exercise" con los datos acordados. Si
             el usuario responde que no, o cambia de tema, no registres nada.
        - Cada mensaje del usuario incluye la fecha y hora actual real entre corchetes (ej.
          "[Fecha y hora actual: 2026-08-03 14:00 (lunes)]"). Úsala como referencia de "hoy"
          al registrar ejercicio (log_exercise) cuando el usuario diga expresiones relativas
          como "hoy", "ayer" o solo una hora sin fecha. No asumas otro día.
        """;

    private const string EstimationInstructions = """
        Eres un experto en fisiología del ejercicio. Dada la descripción de una actividad
        física y su duración en minutos, estima las calorías quemadas para un adulto
        promedio usando valores MET típicos para ese tipo de actividad e intensidad, y
        sugiere un nombre corto y claro en español para el ejercicio (ej. "Caminata rápida",
        "Pesas - tren superior", "Yoga"). No inventes precisión falsa: da tu mejor estimación
        razonable.
        """;

    private readonly ChatClient? _chatClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    public ExerciseAgent(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

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
            throw new InvalidOperationException("ExerciseAgent is not configured (missing Azure OpenAI settings).");
        }

        var logExerciseTool = AIFunctionFactory.Create(
            (string description, int durationMinutes, double? caloriesBurned, string? recordedAtIso, CancellationToken ct) =>
                LogExerciseAsync(azureObjectId, description, durationMinutes, caloriesBurned, recordedAtIso, ct),
            "log_exercise",
            "Registra una actividad física realizada por el usuario en su historial (base de datos), con la " +
            "duración y las calorías quemadas (estimadas por ti). SOLO debe llamarse después de haber " +
            "mostrado tu estimación de calorías y de que el usuario haya confirmado explícitamente que " +
            "quiere agregarlo a su registro - nunca en el mismo turno en que reporta el ejercicio.");

        IList<AITool> tools = [logExerciseTool];
        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: Instructions, name: "ExerciseAgent", tools: tools);
        var session = await GetOrCreateSessionAsync(agent, sessionId, cancellationToken);

        var skill = ExerciseSkillSelector.Select(prompt);
        var skillGuidance = ExerciseSkillLibrary.InstructionsFor(skill);
        var nowLocal = TimeZoneInfo.ConvertTime(DateTime.UtcNow, MealTimeHelper.Central);
        var userLine = string.IsNullOrWhiteSpace(userName) ? string.Empty : $"[Usuario: {userName}]\n";
        var fullPrompt = $"{userLine}[Fecha y hora actual: {nowLocal:yyyy-MM-dd HH:mm} ({nowLocal:dddd})]\n" +
            $"[Guía de skill: {skillGuidance}]\n\nPregunta del usuario: {prompt}";

        var response = await agent.RunAsync(fullPrompt, session, cancellationToken: cancellationToken);
        return response.Text;
    }

    /// <summary>Stateless calorie estimate for a free-text activity description, used by the
    /// "crea tu propio ejercicio" flow on the Ejercicio tab (no chat/session needed there -
    /// the frontend shows this as a preview and only logs it if the user accepts).</summary>
    public async Task<ExerciseEstimationResult> EstimateAsync(string description, int durationMinutes, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("ExerciseAgent is not configured (missing Azure OpenAI settings).");
        }

        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: EstimationInstructions, name: "ExerciseAgent-Estimate");
        var prompt = $"Actividad: \"{description}\"\nDuración: {durationMinutes} minutos.";
        var response = await agent.RunAsync<ExerciseEstimationResult>(prompt, cancellationToken: cancellationToken);
        return response.Result;
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

    private async Task<string> LogExerciseAsync(
        string? azureObjectId,
        [Description("Nombre/descripción breve del ejercicio, ej. 'Caminata' o 'Pesas - tren superior'.")] string description,
        [Description("Duración en minutos.")] int durationMinutes,
        [Description("Calorías quemadas estimadas.")] double? caloriesBurned,
        [Description("Hora en que se realizó, formato ISO 8601 (ej. '2026-08-03T08:30:00'). Si no se especifica, se usa la hora actual.")] string? recordedAtIso,
        CancellationToken cancellationToken)
    {
        var personProvider = _serviceProvider.GetService<DefaultPersonProvider>();
        var dbContextFactory = _serviceProvider.GetService<IDbContextFactory<PersonalAgentDbContext>>();

        if (personProvider is null || dbContextFactory is null)
        {
            return "No se pudo registrar el ejercicio: la base de datos no está configurada.";
        }

        if (string.IsNullOrWhiteSpace(description) || durationMinutes <= 0)
        {
            return "No se pudo registrar el ejercicio: se necesita una descripción y una duración válida.";
        }

        var recordedAt = MealTimeHelper.ParseCentralOrUtcToUtc(recordedAtIso, DateTime.UtcNow);
        var personId = await personProvider.GetOrCreatePersonIdForUserAsync(azureObjectId, cancellationToken);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Saved to THIS person's own catalog (not shared globally, unlike FoodItem) so they
        // can re-log the same activity later without asking the AI to re-estimate it.
        var catalogItem = await PersonalExerciseCatalogHelper.FindOrCreateAsync(
            db, personId, description.Trim(), durationMinutes, caloriesBurned, cancellationToken);

        db.ExerciseLogs.Add(new ExerciseLog
        {
            PersonId = personId,
            Description = catalogItem.Name,
            DurationMinutes = durationMinutes,
            CaloriesBurned = caloriesBurned,
            RecordedAtUtc = recordedAt,
            PersonalExercise = catalogItem,
        });
        await db.SaveChangesAsync(cancellationToken);

        return $"Registrado: {description} ({durationMinutes} min" +
            $"{(caloriesBurned.HasValue ? $", {caloriesBurned.Value:0} kcal" : string.Empty)}) " +
            $"a las {TimeZoneInfo.ConvertTimeFromUtc(recordedAt, MealTimeHelper.Central):HH:mm}.";
    }
}
