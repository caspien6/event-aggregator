using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Model.Domain;
using Model.DTOs;

namespace EventCalendarCollector.Web.Publishing.Google;

public class GoogleCalendarPublisher : ICalendarPublisher
{
    // Google Calendar events only support 11 fixed colors; names outside that
    // palette are aliased to the closest one (e.g. Pumpkin -> Tangerine).
    private static readonly Dictionary<string, string> ColorIdByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lavender"] = "1", ["Sage"] = "2", ["Grape"] = "3", ["Flamingo"] = "4",
        ["Banana"] = "5", ["Tangerine"] = "6", ["Peacock"] = "7", ["Graphite"] = "8",
        ["Blueberry"] = "9", ["Basil"] = "10", ["Tomato"] = "11",
        ["Pumpkin"] = "6"
    };

    private readonly CalendarService _service;
    private readonly string _calendarId;
    private readonly ILogger<GoogleCalendarPublisher> _logger;
    private readonly Dictionary<string, string> _colorIdBySource;

    public GoogleCalendarPublisher(IConfiguration config, ILogger<GoogleCalendarPublisher> logger)
    {
        _logger = logger;
        _calendarId = config["GoogleCalendar:CalendarId"] 
            ?? throw new InvalidOperationException("GoogleCalendar:CalendarId is not configured.");

        var credFile = config["GoogleCalendar:ServiceAccountFile"]
            ?? throw new InvalidOperationException("GoogleCalendar:ServiceAccountFile is not configured.");

        _colorIdBySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in config.GetSection("GoogleCalendar:EventColors").GetChildren())
        {
            if (entry.Value is null)
            {
                continue;
            }
            if (ColorIdByName.TryGetValue(entry.Value, out var colorId))
                _colorIdBySource[entry.Key] = colorId;
            else
                _logger.LogWarning("Unknown event color '{Color}' configured for source {Source}", entry.Value, entry.Key);
        }

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
        ExistingEventDto existing = await FetchExistingEventsAsync(ct);
        
        IReadOnlyList<GoogleScrapedEventDto> googleCalendarEvents = events.Select(e => new GoogleScrapedEventDto(e)).ToList();
        
        foreach (GoogleScrapedEventDto ev in googleCalendarEvents)
        {
            // The same real-world event may already be in the calendar from
            // another scraper under a different name; recognize it by the URLs
            // pointing at the original event.
            (string Id, string? ColorId)? urlMatch = null;
            if (ev.OriginalEventUrls is { Count: > 0 })
            {
                foreach (var candidate in ev.OriginalEventUrls)
                {
                    var key = NormalizeUrlKey(candidate);
                    if (key is not null && existing.ByUrlKey.TryGetValue(key, out var match) && match.Id != ev.GoogleEventId)
                    {
                        urlMatch = match;
                        break;
                    }
                }
            }

            // If this event once got its own entry (e.g. the URL evidence was
            // missing on an earlier run) and now matches another scraper's
            // entry, remove the standalone duplicate.
            if (urlMatch is not null && existing.Ids.Contains(ev.GoogleEventId))
            {
                await _service.Events.Delete(_calendarId, ev.GoogleEventId).ExecuteAsync(ct);
                existing.Ids.Remove(ev.GoogleEventId);
                _logger.LogInformation("Deleted duplicate entry of {Title}; it matches an existing event by URL", ev.Title);
            }

            if (ev.IsCancelled)
            {
                if (existing.Ids.Contains(ev.GoogleEventId))
                {
                    await _service.Events.Delete(_calendarId, ev.GoogleEventId).ExecuteAsync(ct);
                    _logger.LogInformation("Deleted cancelled event: {Title}", ev.Title);
                }
                else if (urlMatch is not null)
                {
                    // The matched entry belongs to another scraper; let that
                    // scraper decide when its own event is cancelled.
                    _logger.LogInformation(
                        "Cancelled event {Title} matches an existing entry by URL; leaving it to its own source", ev.Title);
                }
                continue;
            }

            var targetId = existing.Ids.Contains(ev.GoogleEventId) ? ev.GoogleEventId : urlMatch?.Id ?? ev.GoogleEventId;
            var calEvent = BuildCalendarEvent(ev, targetId, fallbackColorId: urlMatch?.ColorId);

            if (existing.Ids.Contains(targetId))
            {
                await _service.Events.Update(calEvent, _calendarId, targetId).ExecuteAsync(ct);
                if (urlMatch is not null)
                {
                    _logger.LogInformation("Updated existing event matched by URL: {Title}", ev.Title);
                }
                else
                {
                    _logger.LogDebug("Updated event: {Title}", ev.Title);
                }
            }
            else
            {
                try
                {
                    await _service.Events.Insert(calEvent, _calendarId).ExecuteAsync(ct);
                }
                catch (Exception)
                {
                    _service.Events.Delete(_calendarId, ev.GoogleEventId);
                    await _service.Events.Insert(calEvent, _calendarId).ExecuteAsync(ct);
                }
                _logger.LogDebug("Created event: {Title}", ev.Title);
            }
        }
    }

    private async Task<ExistingEventDto> FetchExistingEventsAsync(CancellationToken ct)
    {
        var ids = new HashSet<string>();
        var byUrlKey = new Dictionary<string, (string, string?)>();
        var req = _service.Events.List(_calendarId);
        req.MaxResults = 2500;
        req.SingleEvents = true;
        req.TimeMinDateTimeOffset = DateTimeOffset.UtcNow.AddDays(-1);

        do
        {
            var page = await req.ExecuteAsync(ct);
            foreach (var ev in page.Items ?? [])
            {
                if (ev.Id is null)
                {
                    continue;
                }

                ids.Add(ev.Id);

                // Index every URL the entry's JSON description carries so other
                // scrapers can find it. Entries whose description isn't the JSON
                // format (legacy plain text) are left out of the URL index.
                foreach (var url in ExtractUrlsFromDescription(ev.Description))
                {
                    var key = NormalizeUrlKey(url);
                    if (key is not null)
                    {
                        byUrlKey.TryAdd(key, (ev.Id, ev.ColorId));
                    }
                }
            }
            req.PageToken = page.NextPageToken;
        } while (req.PageToken is not null);

        return new ExistingEventDto
        {
            Ids = ids,
            ByUrlKey = byUrlKey
        };
    }

    // Reduces a URL to "host|last-path-segment" so the same event matches across
    // cosmetic differences (query strings, /events/ vs /jegyvasarlas/event/ paths).
    private static string? NormalizeUrlKey(string url)
    {
        if (!Uri.TryCreate(url.Trim().TrimEnd('.', ',', ')'), UriKind.Absolute, out var uri))
            return null;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
        var lastSegment = uri.AbsolutePath.Trim('/').Split('/').LastOrDefault();
        if (string.IsNullOrEmpty(lastSegment))
            return null;

        return $"{host.ToLowerInvariant()}|{lastSegment.ToLowerInvariant()}";
    }

    private Event BuildCalendarEvent(ScrapedEvent ev, string stableId, string? fallbackColorId = null)
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
            Source = new Event.SourceData { Title = ev.SourceName, Url = ev.Url },
            ColorId = _colorIdBySource.GetValueOrDefault(ev.SourceName) ?? fallbackColorId
        };
    }

    // Relaxed escaping keeps URLs and accented characters verbatim so the
    // description stays readable and round-trips through the URL index.
    private static readonly JsonSerializerOptions DescriptionJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string BuildDescription(ScrapedEvent ev) =>
        JsonSerializer.Serialize(ev, DescriptionJsonOptions);

    private static IReadOnlyList<string> ExtractUrlsFromDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        { 
            return [];
        }

        DescriptionDetails? details;
        
        try
        {
            details = JsonSerializer.Deserialize<DescriptionDetails>(description, DescriptionJsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }

        if (details is null)
        {
            return [];
        }

        var urls = new List<string>();
        
        if (!string.IsNullOrEmpty(details.Url))
        {
            urls.Add(details.Url);
        }

        if (details.OriginalEventUrls is not null)
        {
            urls.AddRange(details.OriginalEventUrls);
        }

        return urls;
    }

    private sealed record DescriptionDetails(string? Url, IReadOnlyList<string>? OriginalEventUrls);
}
