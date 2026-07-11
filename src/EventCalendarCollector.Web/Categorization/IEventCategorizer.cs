using Model.Domain;

namespace EventCalendarCollector.Web.Categorization;

public interface IEventCategorizer
{
    IReadOnlyList<ScrapedEvent> Categorize(IReadOnlyList<ScrapedEvent> events);
}
