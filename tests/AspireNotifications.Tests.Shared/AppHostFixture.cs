using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AspireNotifications.Tests.Shared;

/// <summary>
/// Starts the WHOLE system (Postgres, RabbitMQ, fake API, reader, customer-interface) for tests.
///
/// This is the same recipe as `dotnet run` on the AppHost, but:
///   * the dashboard is OFF (nobody is watching),
///   * it is started ONCE per test run (see the [CollectionDefinition] in each test project),
///   * it is torn down at the end → every run starts from an EMPTY database and EMPTY queues.
///
/// Because of that last point: do NOT run the AppHost in a terminal at the same time as
/// `dotnet test` — you'd have two copies fighting over the build output and the ports.
/// </summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    public const string ExternalApi = "externalapi";
    public const string NotificationReader = "notification-reader";
    public const string CustomerInterface = "customer-interface";
    public const string RabbitMq = "rabbitmq";
    public const string NotificationsDb = "notificationsdb";

    /// <summary>
    /// Want to SEE the dashboard while debugging a test? Set this to true, put a breakpoint
    /// in the failing test, run it in Debug, and open the "Login to the dashboard at ..." URL
    /// printed in the test output. Keep it false normally (and always in CI).
    /// </summary>
    private const bool ShowDashboard = false;

    public DistributedApplication App { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        // The args are forwarded to the AppHost's configuration, exactly like on the command line.
        // We switch the random-notification generator OFF so tests see only what THEY send.
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AspireNotifications_AppHost>(
            args: ["--Generator:Enabled=false"],
            configureBuilder: (options, _) =>
            {
                // The testing builder turns the dashboard OFF by default (nobody is watching in CI).
                options.DisableDashboard = !ShowDashboard;
            });

        // HttpClients created by app.CreateHttpClient(...) get retries/timeouts, like in the services.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        App = await builder.BuildAsync();
        await App.StartAsync();

        // Starting containers takes a while the first time (image pull). Give it a generous budget.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        // Wait for EVERY resource to be healthy before any test runs → no "RabbitMQ not ready yet" flakiness.
        foreach (var resource in new[] { RabbitMq, NotificationsDb, ExternalApi, NotificationReader, CustomerInterface })
        {
            await App.ResourceNotifications.WaitForResourceHealthyAsync(resource, cts.Token);
        }
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }

    // ---- Small conveniences so tests read nicely --------------------------------------

    public HttpClient ExternalApiClient() => App.CreateHttpClient(ExternalApi);
    public HttpClient CustomerInterfaceClient() => App.CreateHttpClient(CustomerInterface);
    public HttpClient NotificationReaderClient() => App.CreateHttpClient(NotificationReader);

    public async Task<RabbitTestClient> ConnectToRabbitAsync()
    {
        var connectionString = await App.GetConnectionStringAsync(RabbitMq)
                               ?? throw new InvalidOperationException("No RabbitMQ connection string");
        return await RabbitTestClient.ConnectAsync(connectionString);
    }

    public async Task<PostgresTestClient> ConnectToPostgresAsync()
    {
        var connectionString = await App.GetConnectionStringAsync(NotificationsDb)
                               ?? throw new InvalidOperationException("No Postgres connection string");
        return new PostgresTestClient(connectionString);
    }
}
