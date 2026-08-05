using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;

namespace PersonalAgent.AzureFunctions;

/// <summary>
/// Centralizes JSON HTTP responses with camelCase serialization - the isolated worker's
/// built-in WriteAsJsonAsync defaults to PascalCase, which breaks a typical frontend JSON
/// contract. Same pattern used in HumanOS's AzureFunctions/Api/FunctionResponseFactory.cs.
/// </summary>
public static class FunctionResponseFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<HttpResponseData> SuccessResponseAsync<T>(HttpRequestData request, T payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOptions));
        return response;
    }

    public static async Task<HttpResponseData> ErrorResponseAsync(HttpRequestData request, string message, HttpStatusCode statusCode)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }, JsonOptions));
        return response;
    }
}
