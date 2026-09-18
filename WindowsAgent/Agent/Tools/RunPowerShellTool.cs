using System.Management.Automation;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsAgent.Configuration;

namespace WindowsAgent.Agent.Tools;

/// <summary>Runs a PowerShell command. This is the highest-blast-radius tool
/// in the set, so it always requires confirmation and runs with a timeout.</summary>
public class RunPowerShellTool : IAgentTool
{
    private readonly AgentOptions _options;

    public RunPowerShellTool(IOptions<AgentOptions> options) => _options = options.Value;

    public string Name => "run_powershell";

    public string Description =>
        "Runs a PowerShell command or short script on the local machine and returns its output. " +
        "Use for file operations, system queries, or anything not covered by a more specific tool.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            command = new { type = "string", description = "The PowerShell command or script to run" }
        },
        required = new[] { "command" }
    };

    public bool RequiresConfirmation => true;

    public string DescribeCall(JsonElement input) =>
        $"Run PowerShell: {Truncate(input.GetProperty("command").GetString() ?? "", 120)}";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var command = input.GetProperty("command").GetString() ?? "";

        using var ps = PowerShell.Create();
        ps.AddScript(command);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.PowerShellTimeoutSeconds));

        try
        {
            var results = await Task.Run(() => ps.Invoke(), cts.Token);

            var output = string.Join(Environment.NewLine, results.Select(r => r?.ToString() ?? ""));

            if (ps.HadErrors)
            {
                var errors = string.Join(Environment.NewLine, ps.Streams.Error.Select(e => e.ToString()));
                return ToolResult.Fail($"PowerShell reported errors: {errors}");
            }

            return ToolResult.Ok(string.IsNullOrWhiteSpace(output) ? "(command completed, no output)" : output);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Fail($"Command timed out after {_options.PowerShellTimeoutSeconds}s.");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"PowerShell execution failed: {ex.Message}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
