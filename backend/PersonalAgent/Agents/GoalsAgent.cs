using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using PersonalAgent.Common;

namespace PersonalAgent.Agents;

/// <summary>
/// Generates a structured, evidence-grounded health goal plan for the Objetivos page: given
/// the user's current stats (weight, height, activity level) and their stated goals in free
/// text, researches real nutrition/exercise plan approaches (via BingPlanSearchProvider, when
/// configured) and returns a single strict-JSON recommendation the frontend renders as a
/// "plan" card (see GoalPlanFunction).
///
/// Not part of OrchestratorAgent's routing - this is a dedicated one-shot generation call
/// from the Objetivos page's form, not a conversational chat turn.
/// </summary>
public sealed class GoalsAgent
{
    private const string Instructions = """
        Eres GoalsAgent, un asistente experto en fijar objetivos de salud (peso, nutrición y
        ejercicio) realistas y basados en evidencia, para una app de seguimiento personal.

        Reglas:
        - Basa tus recomendaciones en principios de nutrición y ejercicio ampliamente
          aceptados (déficit/superávit calórico razonable, ritmo de cambio de peso saludable
          de 0.5-1 kg/semana, al menos 150 min/semana de actividad moderada, etc.). Si se te
          da un resumen de investigación web, úsalo como referencia adicional.
        - Adapta TODO al perfil dado (peso, estatura, IMC, edad, nivel de actividad) y a los
          objetivos concretos que el usuario haya escrito. El plan de ejercicio en particular
          NO debe ser genérico: ajusta intensidad, tipo de ejercicio, progresión y frecuencia
          según la edad de la persona y si su nivel de actividad actual es sedentario o no
          (p. ej. una persona sedentaria o de mayor edad empieza con volumen/intensidad más
          bajos y progresión más gradual que alguien joven y ya activo).
        - No eres un médico ni nutricionista licenciado: para condiciones médicas serias,
          menciona brevemente que debe consultar a un profesional, pero igual da un plan
          general razonable.
        - Responde ÚNICAMENTE con un objeto JSON válido (sin texto adicional, sin markdown,
          sin ```json) con esta forma EXACTA (usa null si algo no aplica, y arrays vacíos si
          no hay items, nunca omitas una clave):
        {
          "summary": string,
          "bmi": number,
          "bmiCategory": string,
          "targetWeightKg": number|null,
          "estimatedWeeksToGoal": number|null,
          "dailyCalorieTarget": number|null,
          "macros": { "proteinGrams": number|null, "carbsGrams": number|null, "fatGrams": number|null },
          "nutritionPlan": { "description": string, "mealsPerDay": number|null, "keyRecommendations": [string] },
          "exercisePlan": { "description": string, "daysPerWeek": number|null, "minutesPerSession": number|null, "keyRecommendations": [string] },
          "milestones": [ { "weekNumber": number, "description": string } ],
          "tips": [string]
        }
        """;

    private readonly ChatClient? _chatClient;
    private readonly BingPlanSearchProvider _bingPlanSearchProvider;

    public GoalsAgent(IConfiguration configuration, BingPlanSearchProvider bingPlanSearchProvider)
    {
        _bingPlanSearchProvider = bingPlanSearchProvider;

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

    public async Task<string> GenerateGoalPlanJsonAsync(
        double weightKg, double heightCm, string activityLevel, string goalsText, int? age, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("GoalsAgent is not configured (missing Azure OpenAI settings).");
        }

        var heightM = heightCm / 100.0;
        var bmi = heightM > 0 ? Math.Round(weightKg / (heightM * heightM), 1) : 0;

        var ageSegment = age is > 0 ? $" Edad: {age} años." : string.Empty;
        var profileSummary =
            $"Peso actual: {weightKg} kg. Estatura: {heightCm} cm. IMC: {bmi}.{ageSegment} " +
            $"Nivel de actividad: {activityLevel}. Objetivos del usuario: {goalsText}";

        string? research = null;
        if (_bingPlanSearchProvider.IsConfigured)
        {
            research = await _bingPlanSearchProvider.SearchNutritionExercisePlanNotesAsync(profileSummary, cancellationToken);
        }

        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: Instructions, name: "GoalsAgent");

        var researchSection = string.IsNullOrWhiteSpace(research)
            ? string.Empty
            : $"\nInvestigación de planes reales encontrados en la web:\n{research}\n";

        var prompt = $"Perfil del usuario:\n{profileSummary}\n{researchSection}\n" +
            "Genera el plan en el formato JSON indicado en tus instrucciones.";

        var response = await agent.RunAsync(prompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
