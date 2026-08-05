using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;

namespace FoodMcpServer;

/// <summary>
/// MCP tools for searching food/nutrition data on the internet.
/// Backed by Open Food Facts (https://world.openfoodfacts.org) - a free, public database
/// that requires no API key.
/// </summary>
[McpServerToolType]
public static class FoodTools
{
    [McpServerTool(Name = "search_food")]
    [Description("Busca un alimento por nombre y devuelve su información nutricional (calorías, proteínas, carbohidratos, grasas por cada 100g) usando la base de datos pública Open Food Facts.")]
    public static async Task<string> SearchFoodAsync(
        IHttpClientFactory httpClientFactory,
        [Description("Nombre del alimento a buscar, por ejemplo 'manzana' o 'pechuga de pollo'.")] string foodName,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PersonalAgent-FoodMcpServer/1.0 (contact: local-dev)");

        var url = $"https://world.openfoodfacts.org/cgi/search.pl?search_terms={Uri.EscapeDataString(foodName)}&search_simple=1&action=process&json=1&page_size=5";

        var result = await client.GetFromJsonAsync<OpenFoodFactsSearchResponse>(url, cancellationToken);

        if (result?.Products is null || result.Products.Count == 0)
        {
            return $"No se encontraron resultados para \"{foodName}\".";
        }

        var lines = result.Products
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductName))
            .Take(5)
            .Select(p =>
                $"- {p.ProductName}" +
                (p.Nutriments is null ? "" :
                    $" | {p.Nutriments.EnergyKcal100g?.ToString("0") ?? "?"} kcal/100g" +
                    $", proteína {p.Nutriments.Proteins100g?.ToString("0.0") ?? "?"} g" +
                    $", carbohidratos {p.Nutriments.Carbohydrates100g?.ToString("0.0") ?? "?"} g" +
                    $", grasas {p.Nutriments.Fat100g?.ToString("0.0") ?? "?"} g"));

        return string.Join("\n", lines);
    }

    private sealed class OpenFoodFactsSearchResponse
    {
        [JsonPropertyName("products")]
        public List<OpenFoodFactsProduct>? Products { get; set; }
    }

    private sealed class OpenFoodFactsProduct
    {
        [JsonPropertyName("product_name")]
        public string? ProductName { get; set; }

        [JsonPropertyName("nutriments")]
        public OpenFoodFactsNutriments? Nutriments { get; set; }
    }

    private sealed class OpenFoodFactsNutriments
    {
        [JsonPropertyName("energy-kcal_100g")]
        public double? EnergyKcal100g { get; set; }

        [JsonPropertyName("proteins_100g")]
        public double? Proteins100g { get; set; }

        [JsonPropertyName("carbohydrates_100g")]
        public double? Carbohydrates100g { get; set; }

        [JsonPropertyName("fat_100g")]
        public double? Fat100g { get; set; }
    }
}
