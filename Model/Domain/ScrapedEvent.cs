namespace Model.Domain;

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
    string? TicketStatus,
    // URLs pointing at the same event on other sites (e.g. the venue's own
    // event page behind a ticket-shop redirect). The publisher uses these to
    // recognize an event that another scraper already put in the calendar
    // under a different name.
    IReadOnlyList<string>? OriginalEventUrls = null
);
