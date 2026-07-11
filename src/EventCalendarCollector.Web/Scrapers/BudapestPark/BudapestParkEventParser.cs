using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp;
using Model.Domain;
using TimeZoneConverter;

namespace EventCalendarCollector.Web.Scrapers.BudapestPark;

public class BudapestParkEventParser
{
    private static readonly TimeZoneInfo BudapestTz =
        TZConvert.GetTimeZoneInfo("Europe/Budapest");

    private readonly ILogger<BudapestParkEventParser> _logger;

    public BudapestParkEventParser(ILogger<BudapestParkEventParser> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedEvent>> ParseAsync(
        string html,
        string sourceName,
        IReadOnlySet<string> includedCategorySlugs)
    {
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        var mount = document.QuerySelector("[data-vue-app='program_book']");
        if (mount is null)
            throw new InvalidOperationException("Program book element ([data-vue-app='program_book']) not found on page.");

        var daysJson = mount.GetAttribute("data-days");
        var categoriesJson = mount.GetAttribute("data-categories");
        if (string.IsNullOrWhiteSpace(daysJson) || string.IsNullOrWhiteSpace(categoriesJson))
            throw new InvalidOperationException("Program book element is missing data-days or data-categories attribute.");

        var categories = JsonSerializer.Deserialize<List<BudapestParkCategory>>(categoriesJson) ?? [];
        var days = JsonSerializer.Deserialize<List<BudapestParkDay>>(daysJson) ?? [];

        var categoriesById = categories
            .Where(c => c.Id != 0)
            .ToDictionary(c => c.Id);

        var events = new List<ScrapedEvent>();
        foreach (var day in days)
        {
            foreach (var item in day.Events)
            {
                if (!categoriesById.TryGetValue(item.CategoryId, out var category)
                    || !includedCategorySlugs.Contains(category.Slug))
                    continue;

                if (item.IsPast)
                    continue;

                var ev = ParseItem(day, item, category, sourceName);
                if (ev is not null)
                    events.Add(ev);
            }
        }

        return events;
    }

    private ScrapedEvent? ParseItem(
        BudapestParkDay day,
        BudapestParkEvent item,
        BudapestParkCategory category,
        string sourceName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(item.Title)) return null;

            var startTime = ParseStartTime(day.Number, item.Time);
            if (startTime is null) return null;

            var slug = item.FrontendUrl?.TrimEnd('/').Split('/').LastOrDefault();
            var sourceId = string.IsNullOrEmpty(slug)
                ? $"budapestpark:{item.Id}"
                : $"budapestpark:{slug}";

            var title = string.IsNullOrWhiteSpace(item.Subtitle)
                ? item.Title.Trim()
                : $"{item.Title.Trim()} – {item.Subtitle.Trim()}";

            // "additional" looks like "Nemzetközi koncertek - Nagyszínpad"; take the stage part
            var stage = item.Additional?.Split(" - ", 2).ElementAtOrDefault(1)?.Trim();
            var venue = string.IsNullOrEmpty(stage) ? "Budapest Park" : $"Budapest Park - {stage}";

            return new ScrapedEvent(
                SourceId: sourceId,
                SourceName: sourceName,
                Title: title,
                StartTime: startTime.Value,
                EndTime: null,
                Venue: venue,
                Description: item.Subtitle?.Trim(),
                Url: item.FrontendUrl ?? "https://www.budapestpark.hu/",
                Genres: [category.Title],
                TicketStatus: MapTicketStatus(item)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Budapest Park event {Id} ({Title})", item.Id, item.Title);
            return null;
        }
    }

    private static DateTimeOffset? ParseStartTime(int dayNumber, string? time)
    {
        // dayNumber format: yyyyMMdd
        var year = dayNumber / 10000;
        var month = dayNumber / 100 % 100;
        var dayOfMonth = dayNumber % 100;
        if (year < 2000 || month is < 1 or > 12 || dayOfMonth is < 1 or > 31) return null;

        // time is the gate-opening time, e.g. "18:00"; fall back to midnight if missing
        if (!TimeOnly.TryParse(time ?? string.Empty, out var timeOfDay))
            timeOfDay = TimeOnly.MinValue;

        var local = new DateTime(year, month, dayOfMonth, timeOfDay.Hour, timeOfDay.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, BudapestTz.GetUtcOffset(local));
    }

    private static string MapTicketStatus(BudapestParkEvent item)
    {
        if (item.IsCancelled) return "Cancelled";
        if (item.IsSoldOut) return "Sold out";
        if (item.IsComingSoon) return string.IsNullOrWhiteSpace(item.ComingSoonText) ? "Coming soon" : item.ComingSoonText;
        if (item.IsFree != 0) return "Free";
        return "Tickets available";
    }

    private sealed record BudapestParkCategory(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("slug")] string Slug
    );

    private sealed record BudapestParkDay(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("events")] List<BudapestParkEvent> Events
    );

    private sealed record BudapestParkEvent(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("subtitle")] string? Subtitle,
        [property: JsonPropertyName("time")] string? Time,
        [property: JsonPropertyName("category_id")] int CategoryId,
        [property: JsonPropertyName("additional")] string? Additional,
        [property: JsonPropertyName("frontend_url")] string? FrontendUrl,
        [property: JsonPropertyName("is_past")] bool IsPast,
        [property: JsonPropertyName("is_free")] int IsFree,
        [property: JsonPropertyName("is_sold_out")] bool IsSoldOut,
        [property: JsonPropertyName("is_cancelled")] bool IsCancelled,
        [property: JsonPropertyName("is_coming_soon")] bool IsComingSoon,
        [property: JsonPropertyName("coming_soon_text")] string? ComingSoonText
    );
}
