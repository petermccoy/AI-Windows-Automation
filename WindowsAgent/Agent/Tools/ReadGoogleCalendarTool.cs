using System.Text.Json;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using WindowsAgent.Google;

namespace WindowsAgent.Agent.Tools;

/// <summary>Read-only view of the connected Google Calendar's primary calendar.
/// Requires a one-time interactive connect via the Settings page — see
/// GoogleAuthService and ReadGmailTool for why this differs from the Graph tools.</summary>
public class ReadGoogleCalendarTool : IAgentTool
{
    private readonly GoogleAuthService _auth;

    public ReadGoogleCalendarTool(GoogleAuthService auth) => _auth = auth;

    public string Name => "read_google_calendar";

    public string Description =>
        "Lists upcoming events on the connected Google account's primary calendar.";

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
        $"Read Google Calendar for the next {DaysAhead(input)} day(s)";

    public async Task<ToolResult> ExecuteAsync(JsonElement input, CancellationToken ct)
    {
        var daysAhead = DaysAhead(input);

        try
        {
            var credential = await _auth.GetCredentialAsync(ct);
            using var calendar = new CalendarService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "WindowsAgent"
            });

            var request = calendar.Events.List("primary");
            request.TimeMinDateTimeOffset = DateTimeOffset.UtcNow;
            request.TimeMaxDateTimeOffset = DateTimeOffset.UtcNow.AddDays(daysAhead);
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.MaxResults = 25;

            var events = await request.ExecuteAsync(ct);
            var items = events.Items ?? new List<Google.Apis.Calendar.v3.Data.Event>();

            if (items.Count == 0)
                return ToolResult.Ok($"No events in the next {daysAhead} day(s).");

            var lines = items.Select(e =>
            {
                var when = e.Start?.Date is { } allDay ? $"{allDay} (all day)" : $"{e.Start?.DateTimeDateTimeOffset} → {e.End?.DateTimeDateTimeOffset}";
                var location = string.IsNullOrWhiteSpace(e.Location) ? "" : $" | {e.Location}";
                return $"- {e.Summary} | {when}{location}";
            });

            return ToolResult.Ok(string.Join("\n", lines));
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to read Google Calendar: {ex.Message}");
        }
    }

    private static int DaysAhead(JsonElement input) =>
        input.TryGetProperty("daysAhead", out var d) ? d.GetInt32() : 7;
}
