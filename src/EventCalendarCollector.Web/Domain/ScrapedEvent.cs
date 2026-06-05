namespace EventCalendarCollector.Web.Domain;

public record ScrapedEvent(
    string SourceId,
    string SourceName,
    string Title,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    string? Venue,
    string? Description,
    string Url,
    IReadOnlyList<string> Genres,
    string? TicketStatus
);
