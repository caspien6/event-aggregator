using EventCalendarCollector.Web.Categorization;
using EventCalendarCollector.Web.Infrastructure;
using EventCalendarCollector.Web.Jobs;
using EventCalendarCollector.Web.Publishing;
using EventCalendarCollector.Web.Publishing.Google;
using EventCalendarCollector.Web.Scrapers.A38;
using EventCalendarCollector.Web.Scrapers.Britishmood;
using EventCalendarCollector.Web.Scrapers.BudapestPark;
using EventCalendarCollector.Web.Scrapers.Kobuci;
using Hangfire;
using Hangfire.MemoryStorage;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);

// Scrapers
builder.Services.AddSingleton<A38EventParser>();
builder.Services.AddScraper<A38Scraper>();
builder.Services.AddSingleton<BudapestParkEventParser>();
builder.Services.AddScraper<BudapestParkScraper>();
builder.Services.AddSingleton<KobuciEventParser>();
builder.Services.AddScraper<KobuciScraper>();
builder.Services.AddSingleton<BritishmoodEventParser>();
builder.Services.AddScraper<BritishmoodScraper>();

// Categorization
builder.Services.AddSingleton<IEventCategorizer, KeywordEventCategorizer>();

// Publisher
builder.Services.AddSingleton<ICalendarPublisher, GoogleCalendarPublisher>();

// Sync job
builder.Services.AddScoped<EventSyncJob>();

// Hangfire
builder.Services.AddHangfire(cfg => cfg.UseMemoryStorage());
builder.Services.AddHangfireServer();

// OpenAPI + Scalar
builder.Services.AddOpenApi();

var app = builder.Build();

// Scalar interactive API UI
app.MapOpenApi();
app.MapScalarApiReference();

// Hangfire dashboard
app.UseHangfireDashboard(app.Configuration["Hangfire:DashboardPath"] ?? "/hangfire");

// Register recurring jobs
RecurringJob.AddOrUpdate<EventSyncJob>(
    "full-sync",
    job => job.RunAsync(CancellationToken.None),
    app.Configuration["Scrapers:A38:CronSchedule"] ?? "0 */6 * * *");

// BudapestPark-only sync, offset from the full sync
RecurringJob.AddOrUpdate<EventSyncJob>(
    "budapestpark-sync",
    job => job.RunSingleAsync("BudapestPark", CancellationToken.None),
    app.Configuration["Scrapers:BudapestPark:CronSchedule"] ?? "0 3-23/6 * * *");

// Kobuci-only sync, offset from the other jobs
RecurringJob.AddOrUpdate<EventSyncJob>(
    "kobuci-sync",
    job => job.RunSingleAsync("Kobuci", CancellationToken.None),
    app.Configuration["Scrapers:Kobuci:CronSchedule"] ?? "0 1-23/6 * * *");

// Britishmood-only sync, offset from the other jobs. Runs shortly before the
// venue syncs would overwrite a shared event, and its URL matching relies on
// the venue events already being in the calendar.
RecurringJob.AddOrUpdate<EventSyncJob>(
    "britishmood-sync",
    job => job.RunSingleAsync("Britishmood", CancellationToken.None),
    app.Configuration["Scrapers:Britishmood:CronSchedule"] ?? "0 2-23/6 * * *");

// Manual trigger endpoints
app.MapPost("/api/sync/run", async (IBackgroundJobClient jobs) =>
{
    var id = jobs.Enqueue<EventSyncJob>(j => j.RunAsync(CancellationToken.None));
    return Results.Ok(new { jobId = id, status = "Enqueued" });
})
.WithName("TriggerFullSync")
.WithSummary("Manually trigger a full sync across all scrapers");

app.MapPost("/api/sync/run/{scraper}", async (string scraper, IBackgroundJobClient jobs) =>
{
    var id = jobs.Enqueue<EventSyncJob>(j => j.RunSingleAsync(scraper, CancellationToken.None));
    return Results.Ok(new { jobId = id, scraper, status = "Enqueued" });
})
.WithName("TriggerScraperSync")
.WithSummary("Manually trigger sync for a single scraper by name (e.g. 'A38')");

app.Run();
