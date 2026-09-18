namespace WindowsAgent.Agent;

public record ToolResult(bool Success, string Output, string? ErrorMessage = null)
{
    public static ToolResult Ok(string output) => new(true, output);
    public static ToolResult Fail(string error) => new(false, "", error);
    public static ToolResult Denied() => new(false, "", "User declined to confirm this action.");
}
