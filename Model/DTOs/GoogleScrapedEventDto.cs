using EventCalendarCollector.BLL.Publishers.Google;
using Model.Domain;

namespace Model.DTOs;

public record GoogleScrapedEventDto : ScrapedEvent
{
    public string GoogleEventId { get; set; }
    public bool IsCancelled { get; init; }

    public GoogleScrapedEventDto(ScrapedEvent abstractEvent) : base(abstractEvent)
    {
        GoogleEventId = abstractEvent.SourceId.ToGoogleEventId();
        IsCancelled = GetIsCancelled(abstractEvent.TicketStatus);
    }

    private bool GetIsCancelled(string? ticketStatus)
    {
        return ticketStatus?.Contains("Cancel", StringComparison.OrdinalIgnoreCase) == true
               || ticketStatus?.Contains("Elmarad", StringComparison.OrdinalIgnoreCase) == true;
    }
}
