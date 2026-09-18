using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using WindowsAgent.Configuration;

namespace WindowsAgent.Agent.Tools;

/// <summary>
/// Sends mail through Microsoft Graph using app-only auth (client credentials),
/// sending as a configured mailbox. Deliberately NOT UI-automating Outlook —
/// Graph is reliable, headless-friendly, and doesn't depend on Outlook being open.
/// Requires an Entra app registration with Mail.Send application permission,
/// admin-consented, scoped via an application access policy to one mailbox.
/// </summary>
public class SendEmailTool : IAgentTool
{
    private readonly GraphOptions _options;
    private GraphServiceClient? _client;

    public SendEmailTool(IOptions<GraphOptions> options) => _options = options.Value;

    public string Name => "send_email";

    public string Description =>
        "Sends an email from the configured mailbox. Use for outbound notifications, " +
        "reports, or messages the user asks to send.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            to = new { type = "string", description = "Recipient email address" },
            subject = new { type = "string" },
            body = new { type = "string", description = "Plain-text or simple HTML body" }
        },
        required = new[] { "to", "subject", "body" }
    };

    public bool RequiresConfirmation => true;

    public string DescribeCall(JsonElement input) =>
        $"Send email to {input.GetProperty("to").GetString()}: \"{input.GetProperty("subject").GetString()}\"";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var to = input.GetProperty("to").GetString() ?? "";
        var subject = input.GetProperty("subject").GetString() ?? "";
        var body = input.GetProperty("body").GetString() ?? "";

        try
        {
            var client = GetClient();

            var message = new Message
            {
                Subject = subject,
                Body = new ItemBody { ContentType = BodyType.Text, Content = body },
                ToRecipients = new List<Recipient>
                {
                    new() { EmailAddress = new EmailAddress { Address = to } }
                }
            };

            await client.Users[_options.SenderUserPrincipalName]
                .SendMail
                .PostAsync(new Microsoft.Graph.Users.Item.SendMail.SendMailPostRequestBody
                {
                    Message = message,
                    SaveToSentItems = true
                }, cancellationToken: ct);

            return ToolResult.Ok($"Email sent to {to}.");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to send email: {ex.Message}");
        }
    }

    private GraphServiceClient GetClient()
    {
        if (_client != null) return _client;

        var credential = new ClientSecretCredential(
            _options.TenantId, _options.ClientId, _options.ClientSecret);

        _client = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
        return _client;
    }
}
