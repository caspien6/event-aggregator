using AngleSharp;
using AngleSharp.Dom;
using EventCalendarCollector.Web.Domain;
using TimeZoneConverter;

namespace EventCalendarCollector.Web.Scrapers.A38;

public class A38EventParser
{
    private static readonly TimeZoneInfo BudapestTz =
        TZConvert.GetTimeZoneInfo("Europe/Budapest");

    private static readonly Dictionary<string, int> MonthMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["January"] = 1, ["February"] = 2, ["March"] = 3, ["April"] = 4,
        ["May"] = 5, ["June"] = 6, ["July"] = 7, ["August"] = 8,
        ["September"] = 9, ["October"] = 10, ["November"] = 11, ["December"] = 12
    };

    public async Task<IReadOnlyList<ScrapedEvent>> ParseAsync(string html, string sourceName)
    {
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        var items = document.QuerySelectorAll("ul li:has(a[href*='/program/'])");
        var events = new List<ScrapedEvent>();

        foreach (var li in items)
        {
            var ev = ParseItem(li, sourceName);
            if (ev is not null)
                events.Add(ev);
        }

        return events;
    }

    private ScrapedEvent? ParseItem(IElement li, string sourceName)
    {
        var anchor = li.QuerySelector("a[href*='/program/']");
        if (anchor is null) return null;

        var href = anchor.GetAttribute("href") ?? string.Empty;
        var slug = href.Split('/').LastOrDefault() ?? href;

        var title = li.QuerySelector("section h2.eventCard__details__title div")?.TextContent.Trim();
        if (string.IsNullOrEmpty(title)) return null;
        // <meta itemprop="startDate" content="2026-06-05T23:00:01+02:00">
        string? startTimeString = li.QuerySelector("meta[itemprop=\"startDate\"]")?.Attributes.FirstOrDefault(x => x.Name == "content")?.Value.Trim();

        if (string.IsNullOrEmpty(startTimeString)) return null;
        var startTime = DateTime.Parse(startTimeString);

        var venue = li.QuerySelector("div.eventHeader-presents span")?.TextContent.Trim();
        var description = li.QuerySelector(".eventCard__details__description p")?.TextContent.Trim();
        var ticketStatus = li.QuerySelector(".eventCard__ticketblock__prices__info eventCard__ticketblock__prices__item--info")?.TextContent.Trim();
        var genres = li.QuerySelectorAll(".genre")
            .Select(g => g.TextContent.Trim())
            .Where(g => !string.IsNullOrEmpty(g))
            .ToList();

        var url = href.StartsWith("http") ? href : $"https://a38.hu{href}";

        return new ScrapedEvent(
            SourceId: $"a38:{slug}",
            SourceName: sourceName,
            Title: title,
            StartTime: startTime,
            EndTime: null,
            Venue: venue,
            Description: description,
            Url: url,
            Genres: genres,
            TicketStatus: ticketStatus
        );
    }

    private DateTimeOffset? ParseDateTime(string monthText, string dayText, string timeText)
    {
        if (!MonthMap.TryGetValue(monthText, out var month)) return null;
        if (!int.TryParse(dayText, out var day)) return null;

        // timeText format: "Friday 23:00" — extract just the time part
        var timePart = timeText.Split(' ').LastOrDefault() ?? string.Empty;
        if (!TimeOnly.TryParse(timePart, out var time)) return null;

        var year = DateTime.UtcNow.Year;
        // If parsed month is before current month, assume next year
        var now = DateTime.UtcNow;
        if (month < now.Month || (month == now.Month && day < now.Day))
            year++;

        var local = new DateTime(year, month, day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, BudapestTz.GetUtcOffset(local));
    }
}
