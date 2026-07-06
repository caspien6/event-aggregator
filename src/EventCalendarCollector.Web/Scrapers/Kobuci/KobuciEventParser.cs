using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using EventCalendarCollector.Web.Domain;
using TimeZoneConverter;

namespace EventCalendarCollector.Web.Scrapers.Kobuci;

public partial class KobuciEventParser
{
    private static readonly TimeZoneInfo BudapestTz =
        TZConvert.GetTimeZoneInfo("Europe/Budapest");

    private readonly ILogger<KobuciEventParser> _logger;

    public KobuciEventParser(ILogger<KobuciEventParser> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedEvent>> ParseAsync(string html, string sourceName)
    {
        var config = Configuration.Default;
        using var context = BrowsingContext.New(config);
        using var document = await context.OpenAsync(req => req.Content(html));

        var items = document.QuerySelectorAll("a[href*='/programok/']:has(.event-list-item)");
        var events = new List<ScrapedEvent>();

        foreach (var anchor in items)
        {
            var ev = ParseItem(anchor, sourceName);
            if (ev is not null)
                events.Add(ev);
        }

        return events;
    }

    private ScrapedEvent? ParseItem(IElement anchor, string sourceName)
    {
        try
        {
            var href = anchor.GetAttribute("href") ?? string.Empty;
            var slug = href.TrimEnd('/').Split('/').LastOrDefault();
            if (string.IsNullOrEmpty(slug)) return null;

            var title = anchor.QuerySelector(".event-name")?.TextContent.Trim();
            if (string.IsNullOrEmpty(title)) return null;

            // The date span reads like "07.08 szerda 19:00" (no year on the page).
            // The h2 usually repeats it, but for special events it holds a status
            // badge instead ("Sold Out!", "Jegyek hamarosan"), so parse the span.
            var dateText = anchor.QuerySelector(".event-name-wrapper span")?.TextContent ?? string.Empty;
            var startTime = ParseStartTime(dateText);
            if (startTime is null)
            {
                _logger.LogWarning("Could not parse date from Kobuci event {Slug}: '{Text}'", slug, dateText.Trim());
                return null;
            }

            var headerBadge = anchor.QuerySelector("h2")?.TextContent.Trim();
            var ticketStatus = headerBadge is not null && !DatePattern().IsMatch(headerBadge)
                ? headerBadge
                : anchor.QuerySelector(".event-status")?.TextContent.Trim();

            return new ScrapedEvent(
                SourceId: $"kobuci:{slug}",
                SourceName: sourceName,
                Title: title,
                StartTime: startTime.Value,
                EndTime: null,
                Venue: "Kobuci Kert",
                Description: null,
                Url: href.StartsWith("http") ? href : $"https://kobuci.hu{href}",
                Genres: [],
                TicketStatus: ticketStatus
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Kobuci event item");
            return null;
        }
    }

    private static DateTimeOffset? ParseStartTime(string headerText)
    {
        var dateMatch = DatePattern().Match(headerText);
        if (!dateMatch.Success) return null;
        var month = int.Parse(dateMatch.Groups[1].Value);
        var day = int.Parse(dateMatch.Groups[2].Value);
        if (month is < 1 or > 12 || day is < 1 or > 31) return null;

        var timeMatch = TimePattern().Match(headerText);
        var hour = timeMatch.Success ? int.Parse(timeMatch.Groups[1].Value) : 0;
        var minute = timeMatch.Success ? int.Parse(timeMatch.Groups[2].Value) : 0;

        // The page shows no year; listings are upcoming, so a date earlier than
        // today (Budapest time) belongs to next year.
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BudapestTz).Date;
        var year = today.Year;
        if (month < today.Month || (month == today.Month && day < today.Day))
            year++;

        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, BudapestTz.GetUtcOffset(local));
    }

    [GeneratedRegex(@"\b(\d{2})\.(\d{2})\b")]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b(\d{1,2}):(\d{2})\b")]
    private static partial Regex TimePattern();
}
