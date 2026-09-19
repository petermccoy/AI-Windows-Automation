using WindowsAgent.Configuration;

namespace WindowsAgent.Orchestrator;

/// <summary>Picks which Claude backend to call based on the live Settings-page
/// selection, so switching providers takes effect on the very next turn.</summary>
public class ClaudeClientFactory
{
    private readonly AnthropicClient _anthropic;
    private readonly BedrockClaudeClient _bedrock;
    private readonly AppSettingsStore _settings;

    public ClaudeClientFactory(AnthropicClient anthropic, BedrockClaudeClient bedrock, AppSettingsStore settings)
    {
        _anthropic = anthropic;
        _bedrock = bedrock;
        _settings = settings;
    }

    public IClaudeClient Current =>
        _settings.Current.Provider == ClaudeProvider.Bedrock ? _bedrock : _anthropic;
}
