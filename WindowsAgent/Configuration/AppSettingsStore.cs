using System.Text.Json;
using Microsoft.Extensions.Options;

namespace WindowsAgent.Configuration;

public enum ClaudeProvider
{
    Anthropic,
    Bedrock
}

public class AppSettings
{
    public ClaudeProvider Provider { get; set; } = ClaudeProvider.Anthropic;
    public AnthropicSettings Anthropic { get; set; } = new();
    public BedrockSettings Bedrock { get; set; } = new();
    public AzureSpeechSettings AzureSpeech { get; set; } = new();
}

public class AnthropicSettings
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-5";
    public int MaxTokens { get; set; } = 2048;
}

public class BedrockSettings
{
    public bool UseDefaultCredentialChain { get; set; } = false;
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
    public string Region { get; set; } = "us-east-1";
    public string ModelId { get; set; } = "";
    public int MaxTokens { get; set; } = 2048;
}

public class AzureSpeechSettings
{
    public string SubscriptionKey { get; set; } = "";
    public string Region { get; set; } = "eastus";
    public string Voice { get; set; } = "en-US-JennyNeural";
}

/// <summary>
/// Single source of truth for everything the Settings page can change at
/// runtime: which Claude provider to call and its credentials/model, plus
/// Azure Speech credentials. Backed by a JSON file outside the publish
/// output (under %LOCALAPPDATA%\WindowsAgent) so edits survive restarts and
/// redeploys without touching appsettings.json. appsettings.json is only
/// read once, to seed a brand-new install that has no settings file yet.
///
/// Clients read <see cref="Current"/> per call rather than capturing values
/// at construction time, so a change on the Settings page takes effect on
/// the very next request with no app restart.
/// </summary>
public class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();
    private AppSettings _current;

    public event Action? Changed;

    public AppSettingsStore(
        IOptions<AnthropicOptions> anthropicDefaults,
        IOptions<BedrockOptions> bedrockDefaults,
        IOptions<AzureSpeechOptions> azureDefaults)
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAgent", "settings.json");

        _current = Load() ?? Seed(anthropicDefaults.Value, bedrockDefaults.Value, azureDefaults.Value);
    }

    public AppSettings Current
    {
        get { lock (_lock) return _current; }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            _current = settings;
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }

        Changed?.Invoke();
    }

    private AppSettings? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_filePath));
        }
        catch (JsonException)
        {
            // Corrupt settings file shouldn't take the whole app down — fall back to seeding.
            return null;
        }
    }

    private static AppSettings Seed(AnthropicOptions anthropic, BedrockOptions bedrock, AzureSpeechOptions azure) => new()
    {
        Provider = ClaudeProvider.Anthropic,
        Anthropic = new AnthropicSettings
        {
            ApiKey = anthropic.ApiKey,
            Model = anthropic.Model,
            MaxTokens = anthropic.MaxTokens
        },
        Bedrock = new BedrockSettings
        {
            UseDefaultCredentialChain = bedrock.UseDefaultCredentialChain,
            AccessKeyId = bedrock.AccessKeyId,
            SecretAccessKey = bedrock.SecretAccessKey,
            Region = bedrock.Region,
            ModelId = bedrock.ModelId,
            MaxTokens = bedrock.MaxTokens
        },
        AzureSpeech = new AzureSpeechSettings
        {
            SubscriptionKey = azure.SubscriptionKey,
            Region = azure.Region,
            Voice = azure.Voice
        }
    };
}
