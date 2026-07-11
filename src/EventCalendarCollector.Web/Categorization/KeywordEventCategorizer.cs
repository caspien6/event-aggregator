using Model.Domain;

namespace EventCalendarCollector.Web.Categorization;

/// <summary>
/// Assigns category names to scraped events by keyword matching on the event
/// title and description. Keywords come from the "EventCategories" config
/// section (category name -> keyword array); categories with no keywords are
/// skipped.
/// </summary>
public class KeywordEventCategorizer : IEventCategorizer
{
    private readonly IReadOnlyDictionary<string, string[]> _keywordsByCategory;
    private readonly ILogger<KeywordEventCategorizer> _logger;

    public KeywordEventCategorizer(IConfiguration config, ILogger<KeywordEventCategorizer> logger)
    {
        _logger = logger;

        var raw = config.GetSection("EventCategories").Get<Dictionary<string, string[]>>()
                  ?? new Dictionary<string, string[]>();

        _keywordsByCategory = raw
            .Where(kvp => kvp.Value is { Length: > 0 })
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        _logger.LogInformation(
            "Loaded {Count} event categories: {Categories}",
            _keywordsByCategory.Count,
            string.Join(", ", _keywordsByCategory.Keys));
    }

    public IReadOnlyList<ScrapedEvent> Categorize(IReadOnlyList<ScrapedEvent> events)
    {
        if (_keywordsByCategory.Count == 0)
        {
            return events;
        }

        return events.Select(CategorizeOne).ToList();
    }

    private ScrapedEvent CategorizeOne(ScrapedEvent ev)
    {
        List<string> matched = [];
        foreach (var (category, keywords) in _keywordsByCategory)
        {
            if (keywords.Any(keyword => Matches(ev, keyword)))
            {
                matched.Add(category);
            }
        }

        return matched.Count == 0 ? ev : ev with { Categories = matched };
    }

    private static bool Matches(ScrapedEvent ev, string keyword)
    {
        return !string.IsNullOrWhiteSpace(keyword)
               && (ev.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                   || ev.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
    }
}
