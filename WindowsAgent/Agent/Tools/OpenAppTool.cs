using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsAgent.Configuration;

namespace WindowsAgent.Agent.Tools;

/// <summary>Opens an application by executable name. Restricted to an allowlist
/// in appsettings so the model can't launch arbitrary binaries.</summary>
public class OpenAppTool : IAgentTool
{
    private readonly AgentOptions _options;

    public OpenAppTool(IOptions<AgentOptions> options) => _options = options.Value;

    public string Name => "open_app";

    public string Description =>
        "Launches a desktop application by executable name (e.g. 'notepad.exe', 'excel.exe'). " +
        "Only apps on the configured allowlist can be opened.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            executable = new { type = "string", description = "Executable name, e.g. 'notepad.exe'" },
            arguments = new { type = "string", description = "Optional command-line arguments" }
        },
        required = new[] { "executable" }
    };

    // Launching apps is low-risk; no confirmation required by default.
    public bool RequiresConfirmation => false;

    public string DescribeCall(JsonElement input) =>
        $"Open {input.GetProperty("executable").GetString()}";

    public Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var exe = input.GetProperty("executable").GetString() ?? "";
        var args = input.TryGetProperty("arguments", out var a) ? a.GetString() ?? "" : "";

        if (!_options.AllowedApps.Contains(exe, StringComparer.OrdinalIgnoreCase))
            return Task.FromResult(ToolResult.Fail($"'{exe}' is not on the allowed-apps list."));

        try
        {
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            return Task.FromResult(ToolResult.Ok($"Launched {exe}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Fail($"Failed to launch {exe}: {ex.Message}"));
        }
    }
}
