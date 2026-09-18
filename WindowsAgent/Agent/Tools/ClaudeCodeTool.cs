using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsAgent.Configuration;

namespace WindowsAgent.Agent.Tools;

/// <summary>
/// Shells out to the Claude Code CLI in headless/print mode to generate or modify
/// code in a working directory. This is intentionally a separate process, not an
/// in-proc SDK call — Claude Code manages its own file edits, tool loop, and
/// permissions inside ClaudeCodeWorkingDirectory.
/// </summary>
public class ClaudeCodeTool : IAgentTool
{
    private readonly AgentOptions _options;

    public ClaudeCodeTool(IOptions<AgentOptions> options) => _options = options.Value;

    public string Name => "generate_code";

    public string Description =>
        "Invokes Claude Code to scaffold a new project or make code changes in the agent's " +
        "working directory. Use for requests like 'build a small app that does X' or " +
        "'add a feature to the project in the workspace'.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            prompt = new { type = "string", description = "What to build or change" }
        },
        required = new[] { "prompt" }
    };

    // Always confirm — this can create/modify a nontrivial number of files.
    public bool RequiresConfirmation => true;

    public string DescribeCall(JsonElement input) =>
        $"Run Claude Code: {input.GetProperty("prompt").GetString()}";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var prompt = input.GetProperty("prompt").GetString() ?? "";

        Directory.CreateDirectory(_options.ClaudeCodeWorkingDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = _options.ClaudeCodeExecutablePath,
            WorkingDirectory = _options.ClaudeCodeWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            return ToolResult.Fail($"Claude Code exited with {process.ExitCode}: {stderr}");

        // stdout is JSON from Claude Code; surface it as-is and let the orchestrator's
        // model summarize it back to the user in the next turn.
        return ToolResult.Ok(stdout);
    }
}
