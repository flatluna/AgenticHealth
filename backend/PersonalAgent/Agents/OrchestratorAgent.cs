using System.ClientModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

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
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    public OrchestratorAgent(
        IConfiguration configuration,
        DietAgent dietAgent,
        ExerciseAgent exerciseAgent,
        PersonalGeneralAgent personalGeneralAgent,
        AdvisorAgent advisorAgent)
    {
        _dietAgent = dietAgent;
        _exerciseAgent = exerciseAgent;
        _personalGeneralAgent = personalGeneralAgent;
        _advisorAgent = advisorAgent;

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

    public async Task<string> AskAsync(string prompt, string sessionId, string? userName = null, CancellationToken cancellationToken = default)
    {
        if (_chatClient is null)
        {
            throw new InvalidOperationException("OrchestratorAgent is not configured (missing Azure OpenAI settings).");
        }

        IList<AITool> tools =
        [
            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario sobre dieta, nutrición o calorías.")] string userMessage) =>
                    _dietAgent.AskAsync(userMessage, sessionId, userName, cancellationToken),
                "ask_diet_agent",
                "Reenvía la pregunta al especialista en dieta, nutrición y conteo de calorías."),

            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario sobre ejercicio o entrenamiento.")] string userMessage) =>
                    _exerciseAgent.AskAsync(userMessage, userName, cancellationToken),
                "ask_exercise_agent",
                "Reenvía la pregunta al especialista en ejercicio y entrenamiento."),

            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario, de tipo personal/general.")] string userMessage) =>
                    _personalGeneralAgent.AskAsync(userMessage, userName, cancellationToken),
                "ask_personal_agent",
                "Reenvía la pregunta al asistente personal general (catch-all)."),

            AIFunctionFactory.Create(
                ([Description("La pregunta original del usuario sobre su historial real: comidas, ejercicio, peso, metas o perfil ya registrados.")] string userMessage) =>
                    _advisorAgent.AskAsync(userMessage, userName, cancellationToken),
                "ask_advisor_agent",
                "Reenvía la pregunta al asesor que consulta el historial REAL guardado del usuario (comidas, ejercicio, peso, metas, perfil)."),
        ];

        var agent = _chatClient.AsIChatClient().AsAIAgent(instructions: Instructions, name: "OrchestratorAgent", tools: tools);
        var session = await GetOrCreateSessionAsync(agent, sessionId, cancellationToken);

        var response = await agent.RunAsync(prompt, session, cancellationToken: cancellationToken);
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
}
