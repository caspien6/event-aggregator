using EventCalendarCollector.Web.Domain;

namespace EventCalendarCollector.Web.Publishing;

public interface ICalendarPublisher
{
    Task PublishAsync(IReadOnlyList<ScrapedEvent> events, CancellationToken ct = default);
}
