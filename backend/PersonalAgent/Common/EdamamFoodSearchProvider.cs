using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace PersonalAgent.Common;

/// <summary>
/// Looks up nutrition facts via Edamam's hosted "Food MCP" server
/// (https://mcp.edamam.com/mcp/food) - a direct structured nutrition API (single HTTP
/// tool call, no LLM/agent thread-and-poll cycle), typically resolving in 1-3s instead of
/// the ~30-50s an Azure AI Foundry Bing Grounding call takes.
///
/// Requires an Edamam Food Database API plan with Food MCP access (paid, e.g. "Enterprise
/// Basic Vision"); credentials are sent per-call as a Bearer token, not stored server-side
/// by Edamam. Maps its response shape into the same normalized JSON field names
/// BingFoodSearchProvider uses (calories/proteinGrams/.../source/query) so DietAgent's
/// existing caching/progress-publishing code works unchanged for either source.
/// </summary>
public sealed class EdamamFoodSearchProvider : IAsyncDisposable
{
    private static readonly Uri EndpointUri = new("https://mcp.edamam.com/mcp/food");
    private const string NutritionToolName = "get_food_nutrition";

    private readonly string? _appId;
    private readonly string? _appKey;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private McpClient? _client;

    public EdamamFoodSearchProvider(IConfiguration configuration)
    {
        _appId = configuration["EdamamAppId"];
        _appKey = configuration["EdamamAppKey"];
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(_appKey);

    /// <summary>Looks up one or several foods in a single MCP tool call, returning a JSON
    /// array (same order as <paramref name="foodDescriptions"/>) using BingFoodSearchProvider's
    /// field names, or null if not configured/the call fails.</summary>
    public async Task<string?> SearchFoodsNutritionJsonAsync(IReadOnlyList<string> foodDescriptions, CancellationToken cancellationToken)
    {
        if (!IsConfigured || foodDescriptions.Count == 0)
        {
            return null;
        }

        try
        {
            var client = await GetOrCreateClientAsync(cancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
            var nutritionTool = tools.FirstOrDefault(t => t.Name == NutritionToolName);
            if (nutritionTool is null)
            {
                return null;
            }

            var result = await nutritionTool.CallAsync(
                new Dictionary<string, object?> { ["queries"] = foodDescriptions.ToArray() },
                cancellationToken: cancellationToken);

            var rawText = string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
            return string.IsNullOrWhiteSpace(rawText) ? null : NormalizeToBingSchema(rawText, foodDescriptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any transport/auth/parsing failure here just falls back to the next search
            // provider (Bing) in DietAgent - never let an Edamam hiccup break the whole reply.
            return null;
        }
    }

    /// <summary>Maps Edamam's get_food_nutrition response (list of {food, macronutrients,
    /// micronutrients, diet_flags, health_flags, status} dicts) into the flat schema
    /// BingFoodSearchProvider produces, pairing each result back to its original query by
    /// position (Edamam echoes results in request order but not the query text itself).</summary>
    private static string NormalizeToBingSchema(string edamamJson, IReadOnlyList<string> foodDescriptions)
    {
        using var doc = JsonDocument.Parse(edamamJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return edamamJson;
        }

        var normalized = new JsonArray();
        var length = Math.Min(doc.RootElement.GetArrayLength(), foodDescriptions.Count);
        for (var i = 0; i < length; i++)
        {
            var item = doc.RootElement[i];
            var status = item.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
            if (status != "ok")
            {
                normalized.Add(new JsonObject { ["query"] = foodDescriptions[i] });
                continue;
            }

            var macro = item.TryGetProperty("macronutrients", out var m) ? m : default;
            var micro = item.TryGetProperty("micronutrients", out var mi) ? mi : default;
            var food = item.TryGetProperty("food", out var f) ? f : default;

            double? Get(JsonElement el, string name) =>
                el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                    ? v.GetDouble()
                    : null;

            string? servingSize = null;
            if (food.ValueKind == JsonValueKind.Object)
            {
                var amount = Get(food, "amount");
                var unit = food.TryGetProperty("unit", out var unitEl) ? unitEl.GetString() : null;
                var weightG = Get(food, "weight_g");
                servingSize = amount is not null && unit is not null
                    ? $"{amount:0.##} {unit}" + (weightG is not null ? $" ({weightG:0}g)" : string.Empty)
                    : null;
            }

            normalized.Add(new JsonObject
            {
                ["query"] = foodDescriptions[i],
                ["servingSize"] = servingSize,
                ["calories"] = Get(macro, "calories_kcal"),
                ["proteinGrams"] = Get(macro, "protein_g"),
                ["carbsGrams"] = Get(macro, "carbs_g"),
                ["fatGrams"] = Get(macro, "fat_g"),
                ["saturatedFatGrams"] = Get(macro, "saturated_fat_g"),
                ["sugarGrams"] = Get(macro, "sugar_g"),
                ["fiberGrams"] = Get(macro, "fiber_g"),
                ["sodiumMilligrams"] = Get(macro, "sodium_mg"),
                ["potassiumMilligrams"] = Get(micro, "potassium_mg"),
                ["calciumMilligrams"] = Get(micro, "calcium_mg"),
                ["ironMilligrams"] = Get(micro, "iron_mg"),
                ["magnesiumMilligrams"] = Get(micro, "magnesium_mg"),
                ["vitaminAMicrograms"] = Get(micro, "vitamin_a_mcg"),
                ["source"] = "Edamam Food Database",
            });
        }

        return normalized.ToJsonString();
    }

    private async Task<McpClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            return _client;
        }

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null)
            {
                return _client;
            }

            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Name = "EdamamFoodMcp",
                Endpoint = EndpointUri,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {_appId}:{_appKey}",
                },
            });

            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            return _client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }
}
