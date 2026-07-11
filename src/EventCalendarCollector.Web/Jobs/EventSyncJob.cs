using EventCalendarCollector.Web.Publishing;
using EventCalendarCollector.Web.Scrapers;
using Model.Domain;

namespace EventCalendarCollector.Web.Jobs;

public class EventSyncJob
{
    private readonly IEnumerable<IEventScraper> _scrapers;
    private readonly ICalendarPublisher _publisher;
    private readonly ILogger<EventSyncJob> _logger;

    public EventSyncJob(
        IEnumerable<IEventScraper> scrapers,
        ICalendarPublisher publisher,
        ILogger<EventSyncJob> logger)
    {
        _scrapers = scrapers;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting full sync across all scrapers");
        foreach (var scraper in _scrapers)
        {
            await RunOneAsync(scraper, ct);
        }
        _logger.LogInformation("Full sync complete");
    }

    public async Task RunSingleAsync(string sourceName, CancellationToken ct)
    {
        var scraper = _scrapers.FirstOrDefault(s =>
            s.SourceName.Equals(sourceName, StringComparison.OrdinalIgnoreCase));

        if (scraper is null)
        {
            _logger.LogWarning("No scraper found for source: {Source}", sourceName);
            return;
        }

        await RunOneAsync(scraper, ct);
    }

    private async Task RunOneAsync(IEventScraper scraper, CancellationToken ct)
    {
        ScraperResult result = await scraper.ScrapeAsync(ct);
        
        if (!result.Success)
        {
            _logger.LogError("Scraper {Source} failed: {Error}", scraper.SourceName, result.ErrorMessage);
            return;
        }

        await _publisher.PublishAsync(result.Events, ct);
    }
}
