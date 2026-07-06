using EventCalendarCollector.Web.Domain;

namespace EventCalendarCollector.Web.Scrapers.Kobuci;

public class KobuciScraper : IEventScraper
{
    public string SourceName => "Kobuci";

    private readonly HttpClient _http;
    private readonly KobuciEventParser _parser;
    private readonly ILogger<KobuciScraper> _logger;
    private readonly string _programsUrl;

    public KobuciScraper(
        IHttpClientFactory httpFactory,
        KobuciEventParser parser,
        ILogger<KobuciScraper> logger,
        IConfiguration config)
    {
        _http = httpFactory.CreateClient(nameof(KobuciScraper));
        _parser = parser;
        _logger = logger;
        _programsUrl = config["Scrapers:Kobuci:ProgramsUrl"] ?? "https://kobuci.hu/programok";
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
