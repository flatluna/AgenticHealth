using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using PersonalAgent.Skills;

namespace PersonalAgent.Agents;

/// <summary>
/// Specialized agent for exercise, training and workout-plan questions.
/// Same self-configuring pattern as DietAgent / HumanOS agents.
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
        """;

    private readonly AIAgent? _agent;

    public ExerciseAgent(IConfiguration configuration)
    {
        var endpoint = configuration["AzureOpenAIEndpoint"];
        var deploymentName = configuration["AzureOpenAIDeploymentName"];
        var apiKey = configuration["AzureOpenAIApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deploymentName))
        {
            _agent = null;
            return;
        }

        AzureOpenAIClient client = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));

        _agent = client.GetChatClient(deploymentName).AsIChatClient().AsAIAgent(instructions: Instructions, name: "ExerciseAgent");
    }

    public bool IsConfigured => _agent is not null;

    public async Task<string> AskAsync(string prompt, string? userName = null, CancellationToken cancellationToken = default)
    {
        if (_agent is null)
        {
            throw new InvalidOperationException("ExerciseAgent is not configured (missing Azure OpenAI settings).");
        }

        var skill = ExerciseSkillSelector.Select(prompt);
        var skillGuidance = ExerciseSkillLibrary.InstructionsFor(skill);
        var userLine = string.IsNullOrWhiteSpace(userName) ? string.Empty : $"[Usuario: {userName}]\n";
        var fullPrompt = $"{userLine}[Guía de skill: {skillGuidance}]\n\nPregunta del usuario: {prompt}";

        var response = await _agent.RunAsync(fullPrompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
