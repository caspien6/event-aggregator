using Model.Domain;
using TimeZoneConverter;

namespace EventCalendarCollector.Web.Scrapers.Britishmood;

public class BritishmoodScraper : IEventScraper
{
    public string SourceName => "Britishmood";

    private static readonly TimeZoneInfo BudapestTz =
        TZConvert.GetTimeZoneInfo("Europe/Budapest");

    private readonly HttpClient _http;
    private readonly BritishmoodEventParser _parser;
    private readonly ILogger<BritishmoodScraper> _logger;
    private readonly string _eventsUrl;

    public BritishmoodScraper(
        IHttpClientFactory httpFactory,
        BritishmoodEventParser parser,
        ILogger<BritishmoodScraper> logger,
        IConfiguration config)
    {
        _http = httpFactory.CreateClient(nameof(BritishmoodScraper));
        // Facebook rejects anonymous browser requests but serves the public
        // page (with events embedded as JSON) to crawler user agents.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            config["Scrapers:Britishmood:UserAgent"] ?? "Googlebot/2.1 (+http://www.google.com/bot.html)");
        _parser = parser;
        _logger = logger;
        _eventsUrl = config["Scrapers:Britishmood:EventsUrl"]
            ?? "https://www.facebook.com/britishmoodofficial/upcoming_hosted_events";
    }

    public async Task<ScraperResult> ScrapeAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Scraping {Source} from {Url}", SourceName, _eventsUrl);
            var listHtml = await _http.GetStringAsync(_eventsUrl, ct);
            var stubs = _parser.ParseEventList(listHtml);
            if (stubs.Count == 0)
                _logger.LogWarning("No events found on {Url} — Facebook may have changed its markup", _eventsUrl);

            var events = new List<ScrapedEvent>();
            foreach (var stub in stubs)
            {
                var ev = await BuildEventAsync(stub, ct);
                if (ev is not null)
                    events.Add(ev);
            }

            _logger.LogInformation("Scraped {Count} events from {Source}", events.Count, SourceName);
            return ScraperResult.Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape {Source}", SourceName);
            return ScraperResult.Fail(ex.Message);
        }
    }

    private async Task<ScrapedEvent?> BuildEventAsync(
        BritishmoodEventParser.FacebookEventStub stub, CancellationToken ct)
    {
        if (stub.StartTimestamp is null)
        {
            _logger.LogWarning("Skipping Facebook event {Id} ({Name}): no start time found", stub.Id, stub.Name);
            return null;
        }

        // Facebook occasionally drops connections mid-response, which the
        // standard resilience handler does not retry, so retry here.
        BritishmoodEventParser.FacebookEventDetail detail = new(null, null, null);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var detailHtml = await _http.GetStringAsync($"https://www.facebook.com/events/{stub.Id}/", ct);
                detail = _parser.ParseEventDetail(detailHtml);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == 3)
                    _logger.LogWarning(ex, "Failed to fetch details for Facebook event {Id} ({Name})", stub.Id, stub.Name);
                else
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }

        // Collect URLs that identify the same event elsewhere: the ticket link
        // (raw and redirect-resolved — ticket shortlinks usually land on the
        // venue's own event page) plus any links in the description.
        var originalUrls = new List<string>();
        if (detail.TicketUrl is not null)
        {
            originalUrls.Add(detail.TicketUrl);
            var resolved = await ResolveFinalUrlAsync(detail.TicketUrl, ct);
            if (resolved is not null && resolved != detail.TicketUrl)
                originalUrls.Add(resolved);
        }
        originalUrls.AddRange(BritishmoodEventParser.ExtractUrls(detail.Description));

        var start = TimeZoneInfo.ConvertTime(
            DateTimeOffset.FromUnixTimeSeconds(stub.StartTimestamp.Value), BudapestTz);
        DateTimeOffset? end = detail.EndTimestamp is { } endTs && endTs > stub.StartTimestamp
            ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(endTs), BudapestTz)
            : null;

        return new ScrapedEvent(
            SourceId: $"britishmood:{stub.Id}",
            SourceName: SourceName,
            Title: stub.Name,
            StartTime: start,
            EndTime: end,
            Venue: stub.Place,
            Description: detail.Description,
            Url: stub.Url,
            Genres: [],
            TicketStatus: stub.IsCanceled ? "Cancelled" : null,
            OriginalEventUrls: originalUrls.Distinct().ToList()
        );
    }

    private async Task<string?> ResolveFinalUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            return response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve ticket URL {Url}", url);
            return null;
        }
    }
}
