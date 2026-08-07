using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PersonalAgent.Common;

/// <summary>
/// Mints short-lived ephemeral client secrets for the Azure OpenAI GPT Realtime API
/// (e.g. "gpt-realtime-mini") via the GA REST endpoint
/// <c>POST {endpoint}openai/v1/realtime/client_secrets</c> — see
/// https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/realtime-audio-webrtc.
/// Ported from HumanOS's RealtimeVoiceSessionService (same proven pattern).
///
/// SECURITY: the real Azure OpenAI API key NEVER leaves this backend. The browser only
/// ever receives the short-lived ephemeral token returned by
/// <see cref="CreateEphemeralSessionAsync"/>, which it then uses directly against Azure's
/// own WebRTC endpoint (<c>openai/v1/realtime/calls</c>) to negotiate a peer-to-peer audio
/// session. This backend never proxies the actual audio stream - it only mints the token
/// and builds the session's <c>instructions</c> text server-side, so the browser can never
/// see or tamper with the system prompt.
/// </summary>
public sealed class RealtimeVoiceSessionService
{
    private readonly HttpClient _httpClient;
    private readonly string? _endpoint;
    private readonly string? _deploymentName;
    private readonly string? _apiKey;
    private readonly string _voice;
    private readonly string _transcriptionModel;

    public RealtimeVoiceSessionService(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(RealtimeVoiceSessionService));
        // Realtime model may live on its own Cognitive Services resource (separate quota/region)
        // - fall back to the main chat endpoint/key when no dedicated one is configured.
        _endpoint = AppConfiguration.GetSetting(configuration, "AzureOpenAIRealtimeEndpoint", "AzureOpenAIEndpoint");
        _deploymentName = configuration["AzureOpenAIRealtimeDeploymentName"];
        _apiKey = AppConfiguration.GetSetting(configuration, "AzureOpenAIRealtimeApiKey", "AzureOpenAIApiKey");
        _voice = configuration["AzureOpenAIRealtimeVoice"] is { Length: > 0 } configuredVoice
            ? configuredVoice
            : "marin";
        _transcriptionModel = configuration["AzureOpenAIRealtimeTranscriptionModel"] is { Length: > 0 } configuredTranscriptionModel
            ? configuredTranscriptionModel
            : "whisper-1";
    }

    /// <summary>
    /// True only when a realtime-capable deployment name and API key are both configured.
    /// Deliberately requires its OWN 'AzureOpenAIRealtimeDeploymentName' setting (NOT the
    /// plain-chat 'AzureOpenAIDeploymentName' every other agent uses) - the Realtime API
    /// only works against a deployment of a realtime-family model (e.g. gpt-realtime-mini),
    /// never a regular chat deployment.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_endpoint)
        && !string.IsNullOrWhiteSpace(_deploymentName)
        && !string.IsNullOrWhiteSpace(_apiKey);

    public sealed class EphemeralSession
    {
        public string ClientSecret { get; set; } = string.Empty;
        public string RealtimeCallsUrl { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Voice { get; set; } = string.Empty;
        public long? ExpiresAtUnixSeconds { get; set; }
    }

    /// <summary>
    /// Mints one ephemeral client secret scoped to a single voice session.
    /// <paramref name="instructions"/> must be built entirely server-side by the caller -
    /// never pass anything derived from unsanitized user input here, since this text
    /// becomes the Realtime session's system prompt.
    /// <paramref name="tools"/> - optional Realtime function-calling tool definitions (each
    /// a JSON object like <c>{ "type": "function", "name": ..., "description": ...,
    /// "parameters": {...} }</c>). When the model decides to call one, Azure sends the
    /// call (name/arguments/call_id) to the BROWSER over the WebRTC data channel - this
    /// backend never executes tools itself, the browser must call back into our REST API
    /// to actually run them and report the result back over the data channel.
    /// </summary>
    public async Task<EphemeralSession> CreateEphemeralSessionAsync(
        string instructions, CancellationToken cancellationToken, System.Text.Json.Nodes.JsonArray? tools = null)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "RealtimeVoiceSessionService is not configured. Set 'AzureOpenAIEndpoint', " +
                "'AzureOpenAIRealtimeDeploymentName' and 'AzureOpenAIApiKey' application settings.");
        }

        var baseUri = _endpoint!.TrimEnd('/');
        var requestUri = $"{baseUri}/openai/v1/realtime/client_secrets";

        var sessionNode = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "realtime",
            ["model"] = _deploymentName,
            ["instructions"] = instructions,
            ["audio"] = new System.Text.Json.Nodes.JsonObject
            {
                ["output"] = new System.Text.Json.Nodes.JsonObject { ["voice"] = _voice },
                ["input"] = new System.Text.Json.Nodes.JsonObject
                {
                    ["transcription"] = new System.Text.Json.Nodes.JsonObject { ["model"] = _transcriptionModel },
                    // GA REST schema nests turn_detection/noise_reduction under audio.input
                    // (NOT at the top level of `session`) - see HumanOS's identical fix.
                    ["turn_detection"] = new System.Text.Json.Nodes.JsonObject
                    {
                        ["type"] = "server_vad",
                        ["threshold"] = 0.85,
                        ["prefix_padding_ms"] = 300,
                        // 900ms (was 700ms) gives a bit more room for a natural mid-sentence
                        // pause (ej. "espagueti blanco... más dos huevos") before treating it
                        // as a finished turn - avoids the model acting on a partial food list.
                        ["silence_duration_ms"] = 900,
                        ["create_response"] = true,
                        ["interrupt_response"] = true
                    },
                    // Suppresses the agent's own voice bleeding back into the mic on
                    // laptop/desktop mic+speaker setups without headphones.
                    ["noise_reduction"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "near_field" }
                }
            }
        };

        if (tools is not null && tools.Count > 0)
        {
            sessionNode["tools"] = tools;
            sessionNode["tool_choice"] = "auto";
        }

        var sessionConfig = new { session = sessionNode };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(JsonSerializer.Serialize(sessionConfig), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Add("api-key", _apiKey);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure OpenAI Realtime client_secrets request failed ({(int)response.StatusCode}): {responseBody}");
        }

        using var parsedResponse = JsonDocument.Parse(responseBody);
        var root = parsedResponse.RootElement;

        if (!root.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"Azure OpenAI Realtime client_secrets response did not contain a 'value' field: {responseBody}");
        }

        long? expiresAt = root.TryGetProperty("expires_at", out var expiresElement)
            && expiresElement.ValueKind == JsonValueKind.Number
                ? expiresElement.GetInt64()
                : null;

        return new EphemeralSession
        {
            ClientSecret = valueElement.GetString()!,
            RealtimeCallsUrl = $"{baseUri}/openai/v1/realtime/calls",
            Model = _deploymentName!,
            Voice = _voice,
            ExpiresAtUnixSeconds = expiresAt
        };
    }
}
