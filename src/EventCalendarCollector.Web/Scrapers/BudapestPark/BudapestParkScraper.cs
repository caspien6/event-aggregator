using EventCalendarCollector.Web.Domain;

namespace EventCalendarCollector.Web.Scrapers.BudapestPark;

public class BudapestParkScraper : IEventScraper
{
    public string SourceName => "BudapestPark";

    private static readonly string[] DefaultCategorySlugs =
        ["esti-koncert", "nemzetkoezi-koncertek", "afterparty"];

    private readonly HttpClient _http;
    private readonly BudapestParkEventParser _parser;
    private readonly ILogger<BudapestParkScraper> _logger;
    private readonly string _programsUrl;
    private readonly IReadOnlySet<string> _includedCategorySlugs;

    public BudapestParkScraper(
        IHttpClientFactory httpFactory,
        BudapestParkEventParser parser,
        ILogger<BudapestParkScraper> logger,
        IConfiguration config)
    {
        _http = httpFactory.CreateClient(nameof(BudapestParkScraper));
        _parser = parser;
        _logger = logger;
        _programsUrl = config["Scrapers:BudapestPark:ProgramsUrl"] ?? "https://www.budapestpark.hu/";
        var slugs = config.GetSection("Scrapers:BudapestPark:IncludedCategorySlugs").Get<string[]>();
        _includedCategorySlugs = (slugs is { Length: > 0 } ? slugs : DefaultCategorySlugs).ToHashSet();
    }

    public async Task<ScraperResult> ScrapeAsync(CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Scraping {Source} from {Url}", SourceName, _programsUrl);
            var html = await _http.GetStringAsync(_programsUrl, ct);
            var events = await _parser.ParseAsync(html, SourceName, _includedCategorySlugs);
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
