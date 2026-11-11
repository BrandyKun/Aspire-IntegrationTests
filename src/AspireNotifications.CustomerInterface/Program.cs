using System.Text.Json.Serialization;
using AspireNotifications.Contracts;
using AspireNotifications.CustomerInterface;
using AspireNotifications.CustomerInterface.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ---- DEPENDENCY 1: RabbitMQ (same name as in the AppHost) ---------------------------
builder.AddRabbitMQClient(Messaging.RabbitConnectionName);

// ---- DEPENDENCY 2: Postgres via EF Core ---------------------------------------------
// "notificationsdb" must match the AppHost: postgres.AddDatabase("notificationsdb").
// Aspire reads ConnectionStrings__notificationsdb and registers NotificationsDbContext
// (scoped) + a health check + tracing of every SQL statement.
builder.AddNpgsqlDbContext<NotificationsDbContext>(NotificationsDbContext.ConnectionName);

// ---- Our own pieces ----------------------------------------------------------------
builder.Services.AddHostedService<NotificationConsumer>(); // the queue listener

// Return enums as text ("High") in JSON instead of numbers (2). Friendlier for humans and tests.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.MapDefaultEndpoints();

// ---- Create the database schema on start-up ----------------------------------------
// EnsureCreated = "if the tables don't exist, create them from the DbContext model".
// Perfect for a sample / tests (fresh container every run). For a real app use EF
// MIGRATIONS instead (dotnet ef migrations add ...) so you can evolve the schema safely.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

// ---- Read endpoints (what the E2E test and your browser use) -----------------------
app.MapGet("/", () => "CustomerInterface is running. See /notifications and /health.");

// Latest 50 stored notifications, newest first.
app.MapGet("/notifications", async (NotificationsDbContext db) =>
    Results.Ok(await db.Notifications
        .OrderByDescending(n => n.ReceivedAt)
        .Take(50)
        .ToListAsync()));

// One notification by the id the EXTERNAL system gave it. 404 until it has been processed.
app.MapGet("/notifications/{externalId}", async (string externalId, NotificationsDbContext db) =>
{
    var item = await db.Notifications.SingleOrDefaultAsync(n => n.ExternalId == externalId);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.Run();
