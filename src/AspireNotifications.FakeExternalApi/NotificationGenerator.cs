namespace AspireNotifications.FakeExternalApi;

/// <summary>
/// Demo helper: every N seconds invent a notification, so the pipeline has something to chew on
/// while you look at the dashboard. Disabled in tests with "--Generator:Enabled=false".
/// </summary>
public sealed class NotificationGenerator(
    NotificationStore store,
    IConfiguration configuration,
    ILogger<NotificationGenerator> logger) : BackgroundService
{
    private static readonly string[] Types = ["ORDER_SHIPPED", "PAYMENT_FAILED", "ACCOUNT_LOCKED", "PROMO"];
    private static readonly string[] Emails = ["anna@example.com", "luca@example.com", "mia@example.com"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var every = TimeSpan.FromSeconds(configuration.GetValue("Generator:IntervalSeconds", 8));
        using var timer = new PeriodicTimer(every);

        logger.LogInformation("Generator ON: a new fake notification every {Seconds}s", every.TotalSeconds);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var type = Types[Random.Shared.Next(Types.Length)];
            var email = Emails[Random.Shared.Next(Emails.Length)];
            var created = store.Add(type, email, $"Auto-generated {type.ToLowerInvariant()} at {DateTimeOffset.UtcNow:HH:mm:ss}");

            logger.LogInformation("Generated notification {Id} ({Type}) for {Email}", created.Id, created.Type, created.CustomerEmail);
        }
    }
}
