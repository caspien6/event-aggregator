using EventCalendarCollector.Web.Domain;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace EventCalendarCollector.Web.Publishing.Google;

public class GoogleCalendarPublisher : ICalendarPublisher
{
    private readonly CalendarService _service;
    private readonly string _calendarId;
    private readonly ILogger<GoogleCalendarPublisher> _logger;

    public GoogleCalendarPublisher(IConfiguration config, ILogger<GoogleCalendarPublisher> logger)
    {
        _logger = logger;
        _calendarId = config["GoogleCalendar:CalendarId"]
            ?? throw new InvalidOperationException("GoogleCalendar:CalendarId is not configured.");

        var credFile = config["GoogleCalendar:ServiceAccountFile"]
            ?? throw new InvalidOperationException("GoogleCalendar:ServiceAccountFile is not configured.");

#pragma warning disable CS0618 // FromJson is deprecated in favour of CredentialFactory, update when that API stabilises
        var credential = GoogleCredential
            .FromJson(File.ReadAllText(credFile))
            .CreateScoped(CalendarService.Scope.Calendar);
#pragma warning restore CS0618

        _service = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "EventCalendarCollector"
        });
    }

    public async Task PublishAsync(IReadOnlyList<ScrapedEvent> events, CancellationToken ct = default)
    {
        var existing = await FetchExistingEventIdsAsync(ct);

        foreach (var ev in events)
        {
            var stableId = ToGoogleEventId(ev.SourceId);
            var isCancelled = ev.TicketStatus?.Contains("Cancel", StringComparison.OrdinalIgnoreCase) == true
                           || ev.TicketStatus?.Contains("Elmarad", StringComparison.OrdinalIgnoreCase) == true;

            if (isCancelled)
            {
                if (existing.Contains(stableId))
                {
                    await _service.Events.Delete(_calendarId, stableId).ExecuteAsync(ct);
                    _logger.LogInformation("Deleted cancelled event: {Title}", ev.Title);
                }
                continue;
            }

            var calEvent = BuildCalendarEvent(ev, stableId);

            if (existing.Contains(stableId))
            {
                await _service.Events.Update(calEvent, _calendarId, stableId).ExecuteAsync(ct);
                _logger.LogDebug("Updated event: {Title}", ev.Title);
            }
            else
            {
                await _service.Events.Insert(calEvent, _calendarId).ExecuteAsync(ct);
                _logger.LogDebug("Created event: {Title}", ev.Title);
            }
        }
    }

    private async Task<HashSet<string>> FetchExistingEventIdsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        var req = _service.Events.List(_calendarId);
        req.MaxResults = 2500;
        req.SingleEvents = true;
        req.TimeMinDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-1);

        do
        {
            var page = await req.ExecuteAsync(ct);
            foreach (var ev in page.Items ?? [])
                if (ev.Id is not null)
                    ids.Add(ev.Id);
            req.PageToken = page.NextPageToken;
        } while (req.PageToken is not null);

        return ids;
    }

    private static Event BuildCalendarEvent(ScrapedEvent ev, string stableId)
    {
        var description = BuildDescription(ev);
        var endTime = ev.EndTime ?? ev.StartTime.AddHours(3);

        return new Event
        {
            Id = stableId,
            Summary = ev.Title,
            Description = description,
            Location = ev.Venue,
            Start = new EventDateTime { DateTimeDateTimeOffset = ev.StartTime },
            End = new EventDateTime { DateTimeDateTimeOffset = endTime },
            Source = new Event.SourceData { Title = ev.SourceName, Url = ev.Url }
        };
    }

    private static string BuildDescription(ScrapedEvent ev)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(ev.Description))
            parts.Add(ev.Description);
        if (ev.Genres.Count > 0)
            parts.Add($"Genres: {string.Join(", ", ev.Genres)}");
        if (!string.IsNullOrEmpty(ev.TicketStatus))
            parts.Add($"Tickets: {ev.TicketStatus}");
        parts.Add(ev.Url);
        return string.Join("\n\n", parts);
    }

    // Google Calendar event IDs must be 5-1024 chars, [a-v0-9] only.
    // Hex chars (0-9, a-f) are a valid subset of that alphabet.
    private static string ToGoogleEventId(string sourceId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sourceId));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
