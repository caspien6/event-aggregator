using EventCalendarCollector.Web.Infrastructure;
using EventCalendarCollector.Web.Jobs;
using EventCalendarCollector.Web.Publishing;
using EventCalendarCollector.Web.Publishing.Google;
using EventCalendarCollector.Web.Scrapers.A38;
using Hangfire;
using Hangfire.MemoryStorage;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);

// Scrapers
builder.Services.AddSingleton<A38EventParser>();
builder.Services.AddScraper<A38Scraper>();

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

// Register recurring job
RecurringJob.AddOrUpdate<EventSyncJob>(
    "full-sync",
    job => job.RunAsync(CancellationToken.None),
    app.Configuration["Scrapers:A38:CronSchedule"] ?? "0 */6 * * *");

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
