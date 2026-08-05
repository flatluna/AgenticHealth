using System.ClientModel;
using System.ComponentModel;
using System.Text.Json;
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
/// "Asesor" (Advisor) agent: answers questions grounded in the user's REAL recorded
/// history (meals, exercise, weight, goals) - e.g. "¿qué comí hoy?" or "¿cómo voy con mi
/// meta de peso?". Unlike DietAgent/ExerciseAgent (general advice), this agent never
/// guesses: it always queries PersonalAgentDB via tool-calling (get_meal_history,
/// get_exercise_history, get_weight_history, get_goals_summary) before answering, so it
/// can cover at least the last month of activity (or any range the user asks about)
/// without needing everything preloaded into the prompt.
/// </summary>
public sealed class AdvisorAgent
{
    private const string Instructions = """
        Eres AsesorAgent, el asesor de salud que responde preguntas sobre el HISTORIAL REAL
        del usuario (qué comió, qué ejercicio hizo, su peso y sus objetivos guardados) - NO
        das recomendaciones nutricionales/de entrenamiento genéricas (eso lo hacen otros
        especialistas), tu trabajo es reportar y comentar datos reales.

        Reglas:
        - Cada mensaje del usuario incluye la fecha y hora actual real entre corchetes (ej.
          "[Fecha y hora actual: 2026-08-04 17:00 (martes)]"). Úsala para calcular los rangos
          de fecha cuando el usuario diga "hoy", "ayer", "esta semana", "este mes", "el mes
          pasado", etc., y pásalos como fechas ISO 8601 (ej. "2026-08-04T00:00:00") a las
          herramientas.
        - SIEMPRE llama a "get_meal_history", "get_exercise_history", "get_weight_history",
          "get_goals_summary" y/o "get_profile_summary" para obtener datos reales ANTES de
          responder. Nunca inventes lo que el usuario comió, hizo de ejercicio, pesó o se
          propuso como meta.
        - Si preguntan por su nombre, estatura o nivel de actividad guardado, usa
          "get_profile_summary". Si el mensaje incluye "[Usuario: ...]" al inicio, ese es
          el nombre real del usuario - úsalo para responder preguntas como "¿cómo me llamo?"
          sin necesidad de llamar a ninguna herramienta para eso.
        - Llama SOLO a la(s) herramienta(s) estrictamente necesaria(s) para responder la
          pregunta concreta - no llames a las cuatro "por si acaso". Ej: si preguntan solo
          por comida, llama únicamente a "get_meal_history"; si preguntan solo por su meta
          de peso, usa "get_goals_summary" y como mucho "get_weight_history". Si necesitas
          más de una herramienta, decide todas las que necesitas de una vez en tu primera
          respuesta en lugar de pedirlas una por una.
        - Si una herramienta no devuelve registros para el rango pedido, dilo claramente
          (ej. "no tengo registros de comidas hoy") en vez de inventar algo.
        - NUNCA inventes cifras (peso objetivo, calorías diarias, macros, hitos, fechas,
          etc.) que no estén literalmente presentes en el JSON devuelto por las
          herramientas. Si "get_goals_summary" no devuelve "latestPlan" o su campo
          "recommendation" es null, dile al usuario que todavía no tiene un plan de
          objetivos generado (sugiérele generarlo en la página Objetivos) en vez de
          suponer valores.
        - Al responder sobre comidas, suma las calorías totales del período y menciona
          brevemente cada comida (tipo y descripción, con sus calorías). Si notas patrones
          que valga la pena señalar (ej. exceso de un mismo alimento, muy pocas calorías,
          poco ejercicio frente a su meta), coméntalo con un tono de cuidado, no alarmista.
        - Responde siempre en español, de forma clara y concisa, en texto plano (nunca JSON
          ni markdown crudo).
        """;

    private readonly ChatClient? _chatClient;
    private readonly IDbContextFactory<PersonalAgentDbContext>? _dbContextFactory;
    private readonly DefaultPersonProvider? _personProvider;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo CentralTimeZone = ResolveCentralTimeZone();

    public AdvisorAgent(
        IConfiguration configuration,
        IDbContextFactory<PersonalAgentDbContext>? dbContextFactory = null,
        DefaultPersonProvider? personProvider = null)
    {
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

    public async Task<string> AskAsync(string prompt, string? userName = null, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("AdvisorAgent is not configured (missing Azure OpenAI settings).");
        }

        IList<AITool> tools =
        [
            AIFunctionFactory.Create(GetMealHistoryAsync, "get_meal_history",
                "Devuelve las comidas registradas por el usuario en un rango de fechas (JSON), con tipo, descripción, calorías y macros."),
            AIFunctionFactory.Create(GetExerciseHistoryAsync, "get_exercise_history",
                "Devuelve los ejercicios registrados por el usuario en un rango de fechas (JSON), con tipo, duración y calorías quemadas."),
            AIFunctionFactory.Create(GetWeightHistoryAsync, "get_weight_history",
                "Devuelve el historial de peso del usuario en un rango de fechas (JSON)."),
            AIFunctionFactory.Create(GetGoalsSummaryAsync, "get_goals_summary",
                "Devuelve las metas activas del usuario y su último plan de objetivos generado (peso objetivo, calorías diarias, etc.) en JSON."),
            AIFunctionFactory.Create(GetProfileSummaryAsync, "get_profile_summary",
                "Devuelve datos guardados del perfil del usuario (estatura y nivel de actividad) en JSON."),
        ];

        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: Instructions, name: "AdvisorAgent", tools: tools);

        var nowLocal = TimeZoneInfo.ConvertTime(DateTime.UtcNow, CentralTimeZone);
        var userLine = string.IsNullOrWhiteSpace(userName) ? string.Empty : $"[Usuario: {userName}]\n";
        var fullPrompt = $"{userLine}[Fecha y hora actual: {nowLocal:yyyy-MM-dd HH:mm} ({nowLocal:dddd})]\n\nPregunta del usuario: {prompt}";

        var response = await agent.RunAsync(fullPrompt, cancellationToken: cancellationToken);
        return response.Text;
    }

    private async Task<string> GetProfileSummaryAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return "La base de datos no está configurada.";
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var person = await db.People
            .Where(p => p.Id == personId)
            .Select(p => new { p.HeightCm, p.CurrentWeightKg, p.ActivityLevel })
            .FirstOrDefaultAsync(cancellationToken);

        return JsonSerializer.Serialize(person, JsonOptions);
    }

    private async Task<string> GetMealHistoryAsync(
        [Description("Fecha/hora inicial ISO 8601 del rango (inclusive). Si se omite, usa hace 30 días.")] string? fromDateIso,
        [Description("Fecha/hora final ISO 8601 del rango (inclusive). Si se omite, usa ahora.")] string? toDateIso,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return "La base de datos no está configurada.";
        }

        var (fromUtc, toUtc) = ResolveRange(fromDateIso, toDateIso, defaultDays: 30);

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var meals = await db.MealLogs
            .Where(m => m.PersonId == personId && m.RecordedAtUtc >= fromUtc && m.RecordedAtUtc <= toUtc)
            .OrderBy(m => m.RecordedAtUtc)
            .Select(m => new
            {
                m.MealType,
                m.Description,
                m.ServingSize,
                m.Calories,
                m.ProteinGrams,
                m.CarbsGrams,
                m.FatGrams,
                RecordedAt = m.RecordedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var result = meals.Select(m => new
        {
            m.MealType,
            m.Description,
            m.ServingSize,
            m.Calories,
            m.ProteinGrams,
            m.CarbsGrams,
            m.FatGrams,
            RecordedAtLocal = ToLocalIso(m.RecordedAt),
        });

        return JsonSerializer.Serialize(new
        {
            totalEntries = meals.Count,
            totalCalories = meals.Sum(m => m.Calories ?? 0),
            entries = result,
        }, JsonOptions);
    }

    private async Task<string> GetExerciseHistoryAsync(
        [Description("Fecha/hora inicial ISO 8601 del rango (inclusive). Si se omite, usa hace 30 días.")] string? fromDateIso,
        [Description("Fecha/hora final ISO 8601 del rango (inclusive). Si se omite, usa ahora.")] string? toDateIso,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return "La base de datos no está configurada.";
        }

        var (fromUtc, toUtc) = ResolveRange(fromDateIso, toDateIso, defaultDays: 30);

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var exercises = await db.ExerciseLogs
            .Where(e => e.PersonId == personId && e.RecordedAtUtc >= fromUtc && e.RecordedAtUtc <= toUtc)
            .OrderBy(e => e.RecordedAtUtc)
            .ToListAsync(cancellationToken);

        var result = exercises.Select(e => new
        {
            e.Description,
            e.DurationMinutes,
            e.CaloriesBurned,
            RecordedAtLocal = ToLocalIso(e.RecordedAtUtc),
        });

        return JsonSerializer.Serialize(new
        {
            totalEntries = exercises.Count,
            totalMinutes = exercises.Sum(e => e.DurationMinutes),
            totalCaloriesBurned = exercises.Sum(e => e.CaloriesBurned ?? 0),
            entries = result,
        }, JsonOptions);
    }

    private async Task<string> GetWeightHistoryAsync(
        [Description("Fecha/hora inicial ISO 8601 del rango (inclusive). Si se omite, usa hace 90 días.")] string? fromDateIso,
        [Description("Fecha/hora final ISO 8601 del rango (inclusive). Si se omite, usa ahora.")] string? toDateIso,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return "La base de datos no está configurada.";
        }

        var (fromUtc, toUtc) = ResolveRange(fromDateIso, toDateIso, defaultDays: 90);

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entries = await db.WeightLogs
            .Where(w => w.PersonId == personId && w.RecordedAtUtc >= fromUtc && w.RecordedAtUtc <= toUtc)
            .OrderBy(w => w.RecordedAtUtc)
            .ToListAsync(cancellationToken);

        var result = entries.Select(w => new { w.WeightKg, RecordedAtLocal = ToLocalIso(w.RecordedAtUtc) });

        return JsonSerializer.Serialize(new
        {
            totalEntries = entries.Count,
            latestWeightKg = entries.Count > 0 ? entries[^1].WeightKg : (double?)null,
            changeKg = entries.Count > 1 ? Math.Round(entries[^1].WeightKg - entries[0].WeightKg, 1) : (double?)null,
            entries = result,
        }, JsonOptions);
    }

    private async Task<string> GetGoalsSummaryAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory is null || _personProvider is null)
        {
            return "La base de datos no está configurada.";
        }

        var personId = await _personProvider.GetOrCreateDefaultPersonIdAsync(cancellationToken);
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var activeGoals = await db.Goals
            .Where(g => g.PersonId == personId && g.Status == GoalStatus.Active)
            .Select(g => new { g.Type, g.Description, g.TargetValue, g.TargetDateUtc })
            .ToListAsync(cancellationToken);

        var latestPlan = await db.GoalPlans
            .Where(p => p.PersonId == personId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Select(p => new { p.WeightKg, p.HeightCm, p.Bmi, p.ActivityLevel, p.GoalsText, p.RecommendationJson, p.CreatedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        return JsonSerializer.Serialize(new
        {
            activeGoals,
            latestPlan = latestPlan is null ? null : new
            {
                latestPlan.WeightKg,
                latestPlan.HeightCm,
                latestPlan.Bmi,
                latestPlan.ActivityLevel,
                latestPlan.GoalsText,
                // The real target weight/calorías/macros/hitos que el usuario ve en la página Objetivos viven aquí.
                Recommendation = ParseRecommendationJson(latestPlan.RecommendationJson),
                CreatedAtLocal = ToLocalIso(latestPlan.CreatedAtUtc),
            },
        }, JsonOptions);
    }

    private static JsonElement? ParseRecommendationJson(string? recommendationJson)
    {
        if (string.IsNullOrWhiteSpace(recommendationJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(recommendationJson).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (DateTime fromUtc, DateTime toUtc) ResolveRange(string? fromDateIso, string? toDateIso, int defaultDays)
    {
        var toUtc = ParseCentralOrUtcToUtc(toDateIso, DateTime.UtcNow);
        var fromUtc = ParseCentralOrUtcToUtc(fromDateIso, toUtc.AddDays(-defaultDays));
        return (fromUtc, toUtc);
    }

    // Naive (no-offset) ISO strings from the LLM represent Central local time (matching the
    // "[Fecha y hora actual: ...]" context it's given) - must be explicitly converted from
    // Central, NOT passed through DateTime.ToUniversalTime(), which assumes the value is
    // already in the SERVER's local timezone (UTC on Azure Linux, silently a no-op there).
    private static DateTime ParseCentralOrUtcToUtc(string? iso, DateTime fallbackUtc)
    {
        if (string.IsNullOrWhiteSpace(iso) ||
            !DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
        {
            return fallbackUtc;
        }

        return parsed.Kind switch
        {
            DateTimeKind.Utc => parsed,
            DateTimeKind.Local => parsed.ToUniversalTime(),
            _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), CentralTimeZone),
        };
    }

    private static string ToLocalIso(DateTime recordedAtUtc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(recordedAtUtc, DateTimeKind.Utc), CentralTimeZone).ToString("yyyy-MM-ddTHH:mm:ss");

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
