using System.Text.Json;
using WindowsAgent.Agent;

namespace WindowsAgent.Orchestrator;

public record AgentEvent(string Kind, string Message); // Kind: "status" | "tool_call" | "tool_result" | "final"

/// <summary>
/// Owns one conversation. Runs the standard Anthropic tool-use loop:
/// send messages -> if stop_reason is tool_use, execute each tool call
/// (gated by confirmation when required) -> feed results back -> repeat
/// until the model returns a plain text turn.
/// </summary>
public class ClaudeAgentOrchestrator
{
    private const string SystemPrompt =
        "You are a Windows desktop assistant with a small set of tools for opening apps, " +
        "running PowerShell, sending email, and generating code via Claude Code. Use the most " +
        "specific tool for the task. Ask a clarifying question in plain text instead of guessing " +
        "when a request is ambiguous (e.g. an email with no recipient). Keep replies brief — " +
        "this is a voice interface.";

    private readonly AnthropicClient _client;
    private readonly ToolRegistry _tools;
    private readonly IConfirmationService _confirmation;
    private readonly List<AnthropicMessage> _history = new();

    public event Action<AgentEvent>? OnEvent;

    public ClaudeAgentOrchestrator(AnthropicClient client, ToolRegistry tools, IConfirmationService confirmation)
    {
        _client = client;
        _tools = tools;
        _confirmation = confirmation;
    }

    /// <summary>Runs one user turn to completion, including any number of tool round-trips.</summary>
    public async Task<string> HandleUserMessageAsync(string userText, CancellationToken ct)
    {
        _history.Add(new AnthropicMessage
        {
            Role = "user",
            Content = new List<ContentBlock> { ContentBlock.Text_(userText) }
        });

        var toolDefs = _tools.ToAnthropicToolDefinitions();

        // Loop: keep going while the model is asking to use tools.
        while (true)
        {
            var response = await _client.CreateMessageAsync(_history, toolDefs, SystemPrompt, ct);

            _history.Add(new AnthropicMessage { Role = "assistant", Content = response.Content });

            var toolUses = response.Content.Where(c => c.Type == "tool_use").ToList();

            if (response.StopReason != "tool_use" || toolUses.Count == 0)
            {
                // Final turn — surface any text blocks back to the caller.
                var finalText = string.Join("\n", response.Content
                    .Where(c => c.Type == "text" && c.Text != null)
                    .Select(c => c.Text));

                OnEvent?.Invoke(new AgentEvent("final", finalText));
                return finalText;
            }

            var resultBlocks = new List<ContentBlock>();

            foreach (var call in toolUses)
            {
                var result = await ExecuteToolCallAsync(call, ct);
                resultBlocks.Add(ContentBlock.ToolResult(call.Id!, result.Success ? result.Output : (result.ErrorMessage ?? "Unknown error"), !result.Success));
            }

            _history.Add(new AnthropicMessage { Role = "user", Content = resultBlocks });
        }
    }

    private async Task<ToolResult> ExecuteToolCallAsync(ContentBlock call, CancellationToken ct)
    {
        if (!_tools.TryGet(call.Name!, out var tool))
        {
            var msg = $"Unknown tool '{call.Name}' requested.";
            OnEvent?.Invoke(new AgentEvent("tool_result", msg));
            return ToolResult.Fail(msg);
        }

        var input = call.Input ?? default;
        var description = tool.DescribeCall(input);

        OnEvent?.Invoke(new AgentEvent("tool_call", description));

        if (tool.RequiresConfirmation)
        {
            var approved = await _confirmation.RequestConfirmationAsync(tool.Name, description, ct);
            if (!approved)
            {
                OnEvent?.Invoke(new AgentEvent("tool_result", $"Declined: {description}"));
                return ToolResult.Denied();
            }
        }

        var result = await tool.ExecuteAsync(input, ct);

        OnEvent?.Invoke(new AgentEvent("tool_result",
            result.Success ? $"{description} → {result.Output}" : $"{description} → FAILED: {result.ErrorMessage}"));

        return result;
    }
}
