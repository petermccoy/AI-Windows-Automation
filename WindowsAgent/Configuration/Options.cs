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

/// <summary>Seed defaults for calling Claude through AWS Bedrock's Converse API
/// instead of the direct Anthropic API. See AppSettingsStore for the live,
/// Settings-page-editable copy of these values.</summary>
public class BedrockOptions
{
    /// <summary>When true, credentials come from the standard AWS chain (environment,
    /// shared config file, IAM role) instead of AccessKeyId/SecretAccessKey below.</summary>
    public bool UseDefaultCredentialChain { get; set; } = false;
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string ModelId { get; set; } = "";
    public int MaxTokens { get; set; } = 2048;
}

public class GraphOptions
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string SenderUserPrincipalName { get; set; } = "";
}

/// <summary>Seed defaults for the Google OAuth "Desktop app" client used to read
/// Gmail/Google Calendar. Unlike Graph, Google has no app-only/service-account
/// path into a personal Gmail account — this is a per-user OAuth client ID/secret
/// from a Google Cloud project, and the actual account access is granted via a
/// one-time interactive consent (see GoogleAuthService).</summary>
public class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

public class AgentOptions
{
    public List<string> AllowedApps { get; set; } = new();
    public int PowerShellTimeoutSeconds { get; set; } = 30;
    public string ClaudeCodeExecutablePath { get; set; } = "claude";
    public string ClaudeCodeWorkingDirectory { get; set; } = "C:\\AgentWorkspace";
}
