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

    /// <summary>Adds CORS headers to the response to allow frontend cross-origin requests.</summary>
    private static void AddCorsHeaders(HttpResponseData response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
    }

    public static async Task<HttpResponseData> SuccessResponseAsync<T>(HttpRequestData request, T payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        AddCorsHeaders(response);
        await response.WriteStringAsync(JsonSerializer.Serialize(payload, JsonOptions));
        return response;
    }

    public static async Task<HttpResponseData> ErrorResponseAsync(HttpRequestData request, string message, HttpStatusCode statusCode)
    {
        var response = request.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        AddCorsHeaders(response);
        await response.WriteStringAsync(JsonSerializer.Serialize(new { error = message }, JsonOptions));
        return response;
    }

    /// <summary>Handles CORS preflight requests (OPTIONS method).</summary>
    public static HttpResponseData PreflightResponseAsync(HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        AddCorsHeaders(response);
        return response;
    }
}
