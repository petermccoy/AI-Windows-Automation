using System.Text.Json;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util;
using WindowsAgent.Google;

namespace WindowsAgent.Agent.Tools;

/// <summary>Read-only view of the connected Gmail account, for finding messages
/// that need follow-up. Requires a one-time interactive connect via the Settings
/// page (see GoogleAuthService) — Google has no app-only path into a personal
/// Gmail account the way Microsoft Graph does for M365.</summary>
public class ReadGmailTool : IAgentTool
{
    private readonly GoogleAuthService _auth;

    public ReadGmailTool(GoogleAuthService auth) => _auth = auth;

    public string Name => "read_gmail";

    public string Description =>
        "Lists recent Gmail messages needing attention. Accepts Gmail search syntax " +
        "(e.g. 'is:unread', 'is:starred', 'from:someone@example.com'). Defaults to unread mail.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string", description = "Gmail search syntax. Defaults to 'is:unread'." },
            maxResults = new { type = "integer", description = "Max messages to return. Defaults to 10." }
        }
    };

    public bool RequiresConfirmation => false;

    public string DescribeCall(JsonElement input) => $"Read Gmail: {Query(input)}";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var query = Query(input);
        var maxResults = input.TryGetProperty("maxResults", out var m) ? m.GetInt32() : 10;

        try
        {
            var credential = await _auth.GetCredentialAsync(ct);
            using var gmail = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "WindowsAgent"
            });

            var listRequest = gmail.Users.Messages.List("me");
            listRequest.Q = query;
            listRequest.MaxResults = maxResults;
            var listResponse = await listRequest.ExecuteAsync(ct);

            if (listResponse.Messages == null || listResponse.Messages.Count == 0)
                return ToolResult.Ok("No matching messages.");

            var lines = new List<string>();
            foreach (var summary in listResponse.Messages)
            {
                var getRequest = gmail.Users.Messages.Get("me", summary.Id);
                getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
                getRequest.MetadataHeaders = new Repeatable<string>(new[] { "From", "Subject", "Date" });
                var message = await getRequest.ExecuteAsync(ct);

                var headers = message.Payload?.Headers;
                string Header(string name) => headers?.FirstOrDefault(h => h.Name == name)?.Value ?? "";

                lines.Add($"- From: {Header("From")} | Subject: {Header("Subject")} | {Header("Date")} | {message.Snippet}");
            }

            return ToolResult.Ok(string.Join("\n", lines));
        }
        catch (InvalidOperationException ex)
        {
            // "not connected yet" from GoogleAuthService — not an operational failure.
            return ToolResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read Gmail: {ex.Message}");
        }
    }

    private static string Query(JsonElement input) =>
        input.TryGetProperty("query", out var q) ? q.GetString() ?? "is:unread" : "is:unread";
}
