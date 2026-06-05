using EventCalendarCollector.Web.Domain;

namespace EventCalendarCollector.Web.Scrapers;

public interface IEventScraper
{
    string SourceName { get; }
    Task<ScraperResult> ScrapeAsync(CancellationToken ct = default);
}

public record ScraperResult(
    bool Success,
    IReadOnlyList<ScrapedEvent> Events,
    string? ErrorMessage = null
)
{
    public static ScraperResult Ok(IReadOnlyList<ScrapedEvent> events) =>
        new(true, events);

    public static ScraperResult Fail(string error) =>
        new(false, Array.Empty<ScrapedEvent>(), error);
}
