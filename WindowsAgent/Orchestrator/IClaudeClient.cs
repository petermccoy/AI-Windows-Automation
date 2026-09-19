using WindowsAgent.Agent;

namespace WindowsAgent.Orchestrator;

/// <summary>
/// One call to a Claude model, regardless of which backend serves it. Both
/// the direct Anthropic API and AWS Bedrock's Converse API implement this
/// against the same internal AnthropicMessage/ContentBlock/AnthropicResponse
/// shapes, so ClaudeAgentOrchestrator and every IAgentTool stay provider-agnostic.
/// </summary>
public interface IClaudeClient
{
    Task<AnthropicResponse> CreateMessageAsync(
        List<AnthropicMessage> messages,
        ToolRegistry tools,
        string systemPrompt,
        CancellationToken ct);
}
