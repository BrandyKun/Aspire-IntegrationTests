using AspireNotifications.Contracts;
using AspireNotifications.NotificationReader;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ---- DEPENDENCY 1: RabbitMQ -------------------------------------------------------
// "rabbitmq" must match the name used in the AppHost: builder.AddRabbitMQ("rabbitmq").
// Aspire reads ConnectionStrings__rabbitmq from the environment and gives us an IConnection.
builder.AddRabbitMQClient(Messaging.RabbitConnectionName);

// ---- DEPENDENCY 2: the external API -------------------------------------------------
// Local run:  the AppHost does .WithReference(externalApi), so "https+http://externalapi"
//             is resolved by service discovery to the fake API's real port.
// ACR / prod: set ExternalApi__BaseUrl to the real URL and service discovery is bypassed.
var externalApiBaseUrl = builder.Configuration["ExternalApi:BaseUrl"] ?? "https+http://externalapi";
builder.Services.AddHttpClient<ExternalApiClient>(client => client.BaseAddress = new Uri(externalApiBaseUrl));

// ---- Our own pieces ----------------------------------------------------------------
builder.Services.AddSingleton<ReaderStats>();              // numbers for /stats
builder.Services.AddSingleton<NotificationPublisher>();    // puts messages on RabbitMQ
builder.Services.AddHostedService<NotificationReaderWorker>(); // the poll loop

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapGet("/", () => "NotificationReader is running. See /stats and /health.");
app.MapGet("/stats", (ReaderStats stats) => Results.Ok(stats.Snapshot()));

app.Run();
