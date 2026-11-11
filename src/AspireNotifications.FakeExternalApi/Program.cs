using AspireNotifications.FakeExternalApi;

var builder = WebApplication.CreateBuilder(args);

// Telemetry + health + service discovery (same line in every service).
builder.AddServiceDefaults();

// One in-memory list of notifications shared by all requests (singleton = one instance for the app).
builder.Services.AddSingleton<NotificationStore>();

// Optional background job that invents a random notification every few seconds,
// so you can just run the app and WATCH things flow. Tests switch it off.
if (builder.Configuration.GetValue("Generator:Enabled", true))
{
    builder.Services.AddHostedService<NotificationGenerator>();
}

var app = builder.Build();
app.MapDefaultEndpoints(); // /health and /alive

// ---- The "external API" surface -------------------------------------------------

app.MapGet("/", () => Results.Ok(new
{
    name = "Fake External Notifications API",
    howToUse = new
    {
        list = "GET  /notifications?afterSequence=0   (what the NotificationReader calls)",
        add = "POST /notifications  { type, customerEmail, message }   (YOU call this to inject one)"
    }
}));

// The reader polls this with the last sequence number it has seen ("give me anything newer").
// This cursor style is how many real APIs work (Stripe events, Slack history, etc.).
app.MapGet("/notifications", (long afterSequence, NotificationStore store) =>
    Results.Ok(store.GetAfter(afterSequence)));

// Test/demo hook: push a notification into the fake system.
app.MapPost("/notifications", (NewNotificationRequest request, NotificationStore store) =>
{
    if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.CustomerEmail))
    {
        return Results.BadRequest("type and customerEmail are required");
    }

    var created = store.Add(request.Type, request.CustomerEmail, request.Message ?? string.Empty, request.Id);
    return Results.Created($"/notifications/{created.Id}", created);
});

app.Run();

/// <summary>Body of POST /notifications. Id is optional: tests pass their own so they can find it later.</summary>
public sealed record NewNotificationRequest(string Type, string CustomerEmail, string? Message, string? Id = null);
