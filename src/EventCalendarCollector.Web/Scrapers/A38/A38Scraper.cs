using EventCalendarCollector.Web.Domain;

namespace EventCalendarCollector.Web.Scrapers.A38;

public class A38Scraper : IEventScraper
{
    public string SourceName => "A38";

    private readonly HttpClient _http;
    private readonly A38EventParser _parser;
    private readonly ILogger<A38Scraper> _logger;
    private readonly string _programsUrl;

    public A38Scraper(
        IHttpClientFactory httpFactory,
        A38EventParser parser,
        ILogger<A38Scraper> logger,
        IConfiguration config)
    {
        _http = httpFactory.CreateClient(nameof(A38Scraper));
        _parser = parser;
        _logger = logger;
        _programsUrl = config["Scrapers:A38:ProgramsUrl"] ?? "https://a38.hu/en/programs";
    }

    public async Task<ScraperResult> ScrapeAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Scraping {Source} from {Url}", SourceName, _programsUrl);
            var html = await _http.GetStringAsync(_programsUrl, ct);
            var events = await _parser.ParseAsync(html, SourceName);
            _logger.LogInformation("Scraped {Count} events from {Source}", events.Count, SourceName);
            return ScraperResult.Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrape {Source}", SourceName);
            return ScraperResult.Fail(ex.Message);
        }
    }
}
