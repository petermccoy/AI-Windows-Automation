using System.Text.Json;

namespace WindowsAgent.Agent.Tools;

/// <summary>Read-only view of the configured mailbox's Outlook/M365 calendar via
/// Microsoft Graph. Shares GraphClientFactory (and its app registration) with
/// SendEmailTool — the app registration additionally needs Calendars.Read
/// granted and admin-consented for this to work.</summary>
public class ReadCalendarTool : IAgentTool
{
    private readonly GraphClientFactory _graph;

    public ReadCalendarTool(GraphClientFactory graph) => _graph = graph;

    public string Name => "read_calendar";

    public string Description =>
        "Lists upcoming events on the configured Outlook/Microsoft 365 calendar. " +
        "Use to check what's scheduled or find open time.";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            daysAhead = new { type = "integer", description = "How many days ahead to look. Defaults to 7." }
        }
    };

    public bool RequiresConfirmation => false;

    public string DescribeCall(JsonElement input) =>
        $"Read Outlook calendar for the next {DaysAhead(input)} day(s)";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var daysAhead = DaysAhead(input);
        var start = DateTimeOffset.UtcNow;
        var end = start.AddDays(daysAhead);

        try
        {
            var client = _graph.GetClient();

            var events = await client.Users[_graph.SenderUserPrincipalName]
                .CalendarView
                .GetAsync(config =>
                {
                    config.QueryParameters.StartDateTime = start.ToString("o");
                    config.QueryParameters.EndDateTime = end.ToString("o");
                    config.QueryParameters.Orderby = new[] { "start/dateTime" };
                    config.QueryParameters.Top = 25;
                }, ct);

            var items = events?.Value ?? new List<Microsoft.Graph.Models.Event>();
            if (items.Count == 0)
                return ToolResult.Ok($"No events in the next {daysAhead} day(s).");

            var lines = items.Select(e =>
                $"- {e.Subject} | {e.Start?.DateTime} → {e.End?.DateTime} ({e.Start?.TimeZone})" +
                (string.IsNullOrWhiteSpace(e.Location?.DisplayName) ? "" : $" | {e.Location!.DisplayName}"));

            return ToolResult.Ok(string.Join("\n", lines));
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read calendar: {ex.Message}");
        }
    }

    private static int DaysAhead(JsonElement input) =>
        input.TryGetProperty("daysAhead", out var d) ? d.GetInt32() : 7;
}
