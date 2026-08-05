using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Agents;

/// <summary>
/// Catch-all agent for general/personal questions that don't fall under diet or exercise.
/// Same self-configuring pattern as DietAgent / ExerciseAgent.
/// </summary>
public sealed class PersonalGeneralAgent
{
    private const string Instructions = """
        Eres un asistente personal general, amable y directo.

        Reglas:
        - Responde siempre en español, de forma clara y concisa.
        - Ayudas con preguntas personales generales que no sean específicamente de dieta/
          nutrición ni de ejercicio/entrenamiento (esas las maneja otro especialista).
        - Si la pregunta del usuario es realmente sobre dieta o ejercicio, indícalo brevemente
          en tu respuesta.
        - Si el mensaje incluye "[Usuario: ...]" al inicio, ese es el nombre real del
          usuario autenticado - úsalo para saludar o responder preguntas como "¿cómo me
          llamo?" directamente, sin decir que no tienes esa información.
        """;

    private readonly AIAgent? _agent;

    public PersonalGeneralAgent(IConfiguration configuration)
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

        _agent = client.GetChatClient(deploymentName).AsIChatClient().AsAIAgent(instructions: Instructions, name: "PersonalGeneralAgent");
    }

    public bool IsConfigured => _agent is not null;

    public async Task<string> AskAsync(string prompt, string? userName = null, CancellationToken cancellationToken = default)
    {
        if (_agent is null)
        {
            throw new InvalidOperationException("PersonalGeneralAgent is not configured (missing Azure OpenAI settings).");
        }

        var userLine = string.IsNullOrWhiteSpace(userName) ? string.Empty : $"[Usuario: {userName}]\n";
        var fullPrompt = $"{userLine}Pregunta del usuario: {prompt}";

        var response = await _agent.RunAsync(fullPrompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
