namespace WindowsAgent.Configuration;

public class AnthropicOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-5";
    public int MaxTokens { get; set; } = 2048;
}

public class AzureSpeechOptions
{
    public string SubscriptionKey { get; set; } = "";
    public string Region { get; set; } = "";
    public string Voice { get; set; } = "en-US-JennyNeural";
}

public class ElevenLabsOptions
{
    public string ApiKey { get; set; } = "";
    public string VoiceId { get; set; } = "";
}

public class GraphOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string SenderUserPrincipalName { get; set; } = "";
}

public class AgentOptions
{
    public List<string> AllowedApps { get; set; } = new();
    public int PowerShellTimeoutSeconds { get; set; } = 30;
    public string ClaudeCodeExecutablePath { get; set; } = "claude";
    public string ClaudeCodeWorkingDirectory { get; set; } = "C:\\AgentWorkspace";
}
