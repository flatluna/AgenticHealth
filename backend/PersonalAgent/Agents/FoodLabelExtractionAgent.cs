using System.ClientModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Agents;

/// <summary>Structured result of extracting a nutrition-facts label from a photo.</summary>
public sealed class FoodLabelExtractionResult
{
    /// <summary>False if the image isn't a food label/package with usable nutrition info (e.g. unrelated photo, too blurry, cropped). When false, the frontend must NOT offer to save it - see <see cref="Reason"/>.</summary>
    public bool IsValidLabel { get; set; }

    /// <summary>Human-readable (Spanish) explanation of why the image isn't usable, filled ONLY when <see cref="IsValidLabel"/> is false.</summary>
    public string? Reason { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Brand { get; set; }

    /// <summary>Tamaño de porción tal como aparece en la etiqueta, ej. "1 taza (240 ml)".</summary>
    public string? ServingSize { get; set; }

    public double? Calories { get; set; }

    public double? ProteinGrams { get; set; }

    public double? CarbsGrams { get; set; }

    public double? FatGrams { get; set; }

    public double? SaturatedFatGrams { get; set; }

    public double? SugarGrams { get; set; }

    public double? FiberGrams { get; set; }

    public double? SodiumMilligrams { get; set; }

    public double? PotassiumMilligrams { get; set; }

    public double? CalciumMilligrams { get; set; }

    public double? IronMilligrams { get; set; }

    public double? MagnesiumMilligrams { get; set; }

    public double? VitaminAMicrograms { get; set; }

    public string? IngredientsText { get; set; }
}

/// <summary>
/// Reads a photo of a food's nutrition-facts label/packaging and asks a vision-capable LLM,
/// via Microsoft Agent Framework (https://learn.microsoft.com/en-us/agent-framework/), to
/// extract the full nutrition information, so the user can review/confirm it before it's
/// logged as a meal - instead of typing every value by hand. Same multimodal DI pattern
/// used elsewhere for image extraction (a <see cref="ChatMessage"/> with a
/// <see cref="TextContent"/> instruction plus a <see cref="DataContent"/> image part,
/// requiring the extra <c>.AsIChatClient()</c> step before <c>.AsAIAgent</c> - plain
/// <c>ChatClient.AsAIAgent(...)</c> doesn't exist on the raw OpenAI SDK type).
/// </summary>
public sealed class FoodLabelExtractionAgent
{
    private const string Instructions = """
        Eres un asistente que extrae información nutricional de una foto de una etiqueta de
        información nutricional (Nutrition Facts) o del empaque de un alimento.

        Primero decide si la imagen es realmente una etiqueta/empaque de un alimento con
        información nutricional COMPLETA y LEGIBLE (al menos calorías y algunos macros
        visibles). Si la imagen NO muestra un alimento, está borrosa, solo muestra una parte
        de la etiqueta, o no tiene información nutricional legible, responde con
        IsValidLabel=false y una explicación breve en español en "Reason" (ej. "La imagen no
        muestra una etiqueta de información nutricional legible."). En ese caso deja los
        demás campos vacíos/nulos.

        Si la imagen SÍ es una etiqueta válida, responde con IsValidLabel=true y extrae:
        - Name: nombre del producto tal como aparece en el empaque.
        - Brand: marca, si es visible.
        - ServingSize: tamaño de porción tal como aparece en la etiqueta (ej. "1 taza (240 ml)").
        - Calories, ProteinGrams, CarbsGrams, FatGrams, SaturatedFatGrams, SugarGrams,
          FiberGrams, SodiumMilligrams, PotassiumMilligrams, CalciumMilligrams,
          IronMilligrams, MagnesiumMilligrams, VitaminAMicrograms: valores exactos POR
          PORCIÓN tal como aparecen en la etiqueta. Deja un campo en null solo si
          genuinamente no aparece en la etiqueta - no inventes números.
        - IngredientsText: la lista de ingredientes tal como aparece, si es visible (puede
          quedar null si no se ve).

        Nunca inventes datos que no puedas leer con confianza en la imagen. Es preferible
        dejar un campo en null a adivinar.
        """;

    private readonly AIAgent? _agent;

    public FoodLabelExtractionAgent(IConfiguration configuration)
    {
        var endpoint = configuration["AzureOpenAIEndpoint"];
        var deploymentName = configuration["AzureOpenAIVisionDeploymentName"] ?? configuration["AzureOpenAIDeploymentName"];
        var apiKey = configuration["AzureOpenAIApiKey"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(deploymentName))
        {
            _agent = null;
            return;
        }

        AzureOpenAIClient client = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
            : new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));

        _agent = client
            .GetChatClient(deploymentName)
            .AsIChatClient()
            .AsAIAgent(instructions: Instructions, name: "FoodLabelExtractionAgent");
    }

    public bool IsConfigured => _agent is not null;

    public async Task<FoodLabelExtractionResult> ExtractAsync(
        byte[] imageBytes,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (_agent is null)
        {
            throw new InvalidOperationException(
                "El agente de extracción de etiquetas no está configurado (faltan 'AzureOpenAIEndpoint'/'AzureOpenAIDeploymentName').");
        }

        var imageContent = new DataContent(imageBytes, contentType)
        {
            AdditionalProperties = new() { ["detail"] = "high" },
        };

        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent("Extrae la información nutricional completa de esta foto de una etiqueta/empaque de alimento."),
            imageContent,
        ]);

        var response = await _agent.RunAsync<FoodLabelExtractionResult>(message, cancellationToken: cancellationToken);
        return response.Result;
    }
}
