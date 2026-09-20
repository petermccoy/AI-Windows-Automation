using System.Text.Json;

namespace WindowsAgent.Agent.Tools;

/// <summary>Read-only view of the configured mailbox's Outlook/M365 inbox via
/// Microsoft Graph, for finding messages that need follow-up. Shares
/// GraphClientFactory (and its app registration) with SendEmailTool — the app
/// registration additionally needs Mail.Read granted and admin-consented.</summary>
public class ReadEmailTool : IAgentTool
{
    private readonly GraphClientFactory _graph;

    public ReadEmailTool(GraphClientFactory graph) => _graph = graph;

    public string Name => "read_email";

    public string Description =>
        "Lists recent messages in the configured Outlook/Microsoft 365 inbox that need " +
        "attention. Use 'unread' or 'flagged' to find things to follow up on.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filter = new
            {
                type = "string",
                @enum = new[] { "unread", "flagged", "all" },
                description = "Which messages to list. Defaults to 'unread'."
            },
            maxResults = new { type = "integer", description = "Max messages to return. Defaults to 10." }
        }
    };

    public bool RequiresConfirmation => false;

    public string DescribeCall(JsonElement input) =>
        $"Read Outlook inbox: {Filter(input)}";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var filter = Filter(input);
        var maxResults = input.TryGetProperty("maxResults", out var m) ? m.GetInt32() : 10;

        try
        {
            var client = _graph.GetClient();

            var messages = await client.Users[_graph.SenderUserPrincipalName]
                .Messages
                .GetAsync(config =>
                {
                    config.QueryParameters.Filter = filter switch
                    {
                        "unread" => "isRead eq false",
                        "flagged" => "flag/flagStatus eq 'flagged'",
                        _ => null
                    };
                    config.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
                    config.QueryParameters.Top = maxResults;
                    config.QueryParameters.Select = new[] { "from", "subject", "receivedDateTime", "bodyPreview", "isRead" };
                }, ct);

            var items = messages?.Value ?? new List<Microsoft.Graph.Models.Message>();
            if (items.Count == 0)
                return ToolResult.Ok($"No {filter} messages.");

            var lines = items.Select(msg =>
                $"- From: {msg.From?.EmailAddress?.Name} <{msg.From?.EmailAddress?.Address}> | " +
                $"Subject: {msg.Subject} | {msg.ReceivedDateTime:u} | {msg.BodyPreview}");

            return ToolResult.Ok(string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read email: {ex.Message}");
        }
    }

    private static string Filter(JsonElement input) =>
        input.TryGetProperty("filter", out var f) ? f.GetString() ?? "unread" : "unread";
}
