using EventCalendarCollector.Web.Scrapers;
using Microsoft.Extensions.Http.Resilience;

namespace EventCalendarCollector.Web.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScraper<T>(this IServiceCollection services)
        where T : class, IEventScraper
    {
        services.AddSingleton<T>();
        services.AddSingleton<IEventScraper>(sp => sp.GetRequiredService<T>());
        services.AddHttpClient(typeof(T).Name)
            .AddStandardResilienceHandler();
        return services;
    }
}
