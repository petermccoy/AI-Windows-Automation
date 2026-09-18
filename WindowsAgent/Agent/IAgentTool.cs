using System.Text.Json;

namespace WindowsAgent.Agent;

/// <summary>
/// One discrete, named action the agent can take. Each tool maps 1:1 to a
/// Claude tool-use definition — keep the surface small and specific rather
/// than exposing one generic "run anything" tool.
/// </summary>
public interface IAgentTool
{
    /// <summary>Stable name sent to Claude, e.g. "open_app".</summary>
    string Name { get; }

    /// <summary>Plain-language description Claude uses to decide when to call this.</summary>
    string Description { get; }

    /// <summary>JSON Schema (as a JsonElement/object) describing the tool's input.</summary>
    object InputSchema { get; }

    /// <summary>
    /// If true, the orchestrator must get explicit user confirmation via
    /// IConfirmationService before ExecuteAsync runs.
    /// </summary>
    bool RequiresConfirmation { get; }

    /// <summary>
    /// One-line, human-readable summary of what this specific call will do,
    /// shown in the confirmation dialog (e.g. "Send email to bob@stgp.com:
    /// 'Q3 numbers'"). Built from the parsed input, not just the tool name.
    /// </summary>
    string DescribeCall(JsonElement input);

    Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct);
}
