
using Model.Domain;

namespace EventCalendarCollector.Web.Scrapers;

public interface IEventScraper
{
    string SourceName { get; }
    Task<ScraperResult> ScrapeAsync(CancellationToken ct = default);
}
