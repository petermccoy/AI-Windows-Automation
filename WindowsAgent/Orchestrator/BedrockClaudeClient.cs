using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Amazon.Runtime.Documents;
using WindowsAgent.Agent;
using WindowsAgent.Configuration;
using AwsContentBlock = Amazon.BedrockRuntime.Model.ContentBlock;
using AwsMessage = Amazon.BedrockRuntime.Model.Message;

namespace WindowsAgent.Orchestrator;

/// <summary>
/// Talks to Claude through AWS Bedrock's model-agnostic Converse API instead
/// of the direct Anthropic API, for anyone who'd rather route through their
/// AWS account/credentials than hold a separate Anthropic API key. Translates
/// to/from the same internal AnthropicMessage/ContentBlock/AnthropicResponse
/// shapes AnthropicClient uses, so the orchestrator and every IAgentTool
/// don't need to know which provider is live.
///
/// Builds a fresh Bedrock client per call (rather than caching one) so a
/// credential/region change on the Settings page takes effect immediately —
/// Bedrock calls aren't frequent enough for the construction cost to matter.
/// </summary>
public class BedrockClaudeClient : IClaudeClient
{
    private readonly AppSettingsStore _settings;

    public BedrockClaudeClient(AppSettingsStore settings) => _settings = settings;

    public async Task<AnthropicResponse> CreateMessageAsync(
        List<AnthropicMessage> messages,
        ToolRegistry tools,
        string systemPrompt,
        CancellationToken ct)
    {
        var settings = _settings.Current.Bedrock;

        using var client = BuildClient(settings);

        var request = new Amazon.BedrockRuntime.Model.ConverseRequest
        {
            ModelId = settings.ModelId,
            System = new List<Amazon.BedrockRuntime.Model.SystemContentBlock> { new() { Text = systemPrompt } },
            Messages = messages.Select(ToBedrockMessage).ToList(),
            InferenceConfig = new Amazon.BedrockRuntime.Model.InferenceConfiguration { MaxTokens = settings.MaxTokens },
            ToolConfig = new Amazon.BedrockRuntime.Model.ToolConfiguration
            {
                Tools = tools.All.Select(t => new Amazon.BedrockRuntime.Model.Tool
                {
                    ToolSpec = new Amazon.BedrockRuntime.Model.ToolSpecification
                    {
                        Name = t.Name,
                        Description = t.Description,
                        InputSchema = new Amazon.BedrockRuntime.Model.ToolInputSchema
                        {
                            Json = Document.FromObject(t.InputSchema)
                        }
                    }
                }).ToList()
            }
        };

        var response = await client.ConverseAsync(request, ct);

        return new AnthropicResponse
        {
            Content = response.Output.Message.Content.Select(FromBedrockContentBlock).ToList(),
            StopReason = response.StopReason == StopReason.Tool_use ? "tool_use" : "end_turn"
        };
    }

    private static IAmazonBedrockRuntime BuildClient(BedrockSettings settings)
    {
        var region = RegionEndpoint.GetBySystemName(settings.Region);

        return settings.UseDefaultCredentialChain
            ? new AmazonBedrockRuntimeClient(region)
            : new AmazonBedrockRuntimeClient(new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey), region);
    }

    private static AwsMessage ToBedrockMessage(AnthropicMessage message) => new()
    {
        Role = message.Role == "assistant" ? ConversationRole.Assistant : ConversationRole.User,
        Content = message.Content.Select(ToBedrockContentBlock).ToList()
    };

    private static AwsContentBlock ToBedrockContentBlock(ContentBlock block) => block.Type switch
    {
        "tool_use" => new AwsContentBlock
        {
            ToolUse = new Amazon.BedrockRuntime.Model.ToolUseBlock
            {
                ToolUseId = block.Id,
                Name = block.Name,
                Input = ToDocument(block.Input)
            }
        },
        "tool_result" => new AwsContentBlock
        {
            ToolResult = new Amazon.BedrockRuntime.Model.ToolResultBlock
            {
                ToolUseId = block.ToolUseId,
                Content = new List<Amazon.BedrockRuntime.Model.ToolResultContentBlock> { new() { Text = block.ResultContent ?? "" } },
                Status = block.IsError == true ? ToolResultStatus.Error : ToolResultStatus.Success
            }
        },
        _ => new AwsContentBlock { Text = block.Text ?? "" }
    };

    private static ContentBlock FromBedrockContentBlock(AwsContentBlock block) =>
        block.ToolUse != null
            ? new ContentBlock
            {
                Type = "tool_use",
                Id = block.ToolUse.ToolUseId,
                Name = block.ToolUse.Name,
                Input = FromDocument(block.ToolUse.Input)
            }
            : ContentBlock.Text_(block.Text ?? "");

    // Document (Amazon.Runtime.Documents.Document) carries a [JsonConverter] attribute,
    // so round-tripping it through System.Text.Json is the supported way to bridge it
    // with our JsonElement-based ContentBlock.Input — there's no direct Document<->JsonElement
    // conversion in the SDK.
    private static Document ToDocument(JsonElement? input) =>
        JsonSerializer.Deserialize<Document>(input?.GetRawText() ?? "{}");

    private static JsonElement FromDocument(Document document) =>
        JsonDocument.Parse(JsonSerializer.Serialize(document)).RootElement;
}
