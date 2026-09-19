using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsAgent.Agent;
using WindowsAgent.Configuration;

namespace WindowsAgent.Orchestrator;

/// <summary>Thin wrapper over POST /v1/messages, including tool-use support.
/// There's no official Anthropic .NET SDK, so this talks HTTP directly.</summary>
public class AnthropicClient : IClaudeClient
{
    // ContentBlock carries every field any block type might use (text, tool_use,
    // tool_result), so most of them are null on any given instance. Without
    // WhenWritingNull, System.Text.Json would serialize those as explicit
    // "field": null entries — e.g. a tool_use block would round-trip with
    // "content": null, "is_error": null attached — which risks the Anthropic
    // API rejecting the request as malformed.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly AppSettingsStore _settings;

    public AnthropicClient(HttpClient http, AppSettingsStore settings)
    {
        _settings = settings;
        http.BaseAddress = new Uri("https://api.anthropic.com/");
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http = http;
    }

    public async Task<AnthropicResponse> CreateMessageAsync(
        List<AnthropicMessage> messages,
        ToolRegistry tools,
        string systemPrompt,
        CancellationToken ct)
    {
        var settings = _settings.Current.Anthropic;

        var request = new
        {
            model = settings.Model,
            max_tokens = settings.MaxTokens,
            system = systemPrompt,
            messages,
            tools = tools.ToAnthropicToolDefinitions()
        };

        // API key is attached per-request (not in DefaultRequestHeaders set once in the
        // constructor) so a key changed on the Settings page takes effect immediately.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(request, options: SerializerOptions)
        };
        httpRequest.Headers.Add("x-api-key", settings.ApiKey);

        var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AnthropicResponse>(SerializerOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response from Anthropic API.");
    }
}

// --- Minimal request/response models covering text + tool_use/tool_result blocks ---

public class AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; set; } = new();
}

public class ContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "text" | "tool_use" | "tool_result"

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; } // tool_use id

    [JsonPropertyName("name")]
    public string? Name { get; set; } // tool name

    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; } // tool_use input

    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; } // for tool_result blocks

    [JsonPropertyName("content")]
    public string? ResultContent { get; set; } // for tool_result blocks

    [JsonPropertyName("is_error")]
    public bool? IsError { get; set; }

    public static ContentBlock Text_(string text) => new() { Type = "text", Text = text };

    public static ContentBlock ToolResult(string toolUseId, string content, bool isError) => new()
    {
        Type = "tool_result",
        ToolUseId = toolUseId,
        ResultContent = content,
        IsError = isError
    };
}

public class AnthropicResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; set; } = new();

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; } // "end_turn" | "tool_use" | ...
}
