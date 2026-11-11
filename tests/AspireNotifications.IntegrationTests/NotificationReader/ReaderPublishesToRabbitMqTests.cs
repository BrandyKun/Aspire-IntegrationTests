using System.Net.Http.Json;

namespace AspireNotifications.IntegrationTests.NotificationReader;

/// <summary>
/// INTEGRATION TEST for the NotificationReader.
///
///   external API ──► notification-reader ──► RabbitMQ exchange ──► [our spy queue]
///
/// We touch the reader's INPUT (the fake API) and watch the reader's OUTPUT (RabbitMQ).
/// We do NOT look at Postgres or the CustomerInterface — that's a different service's job.
/// </summary>
[Collection(AppHostCollection.Name)]
public class ReaderPublishesToRabbitMqTests(AppHostFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task New_notification_in_external_api_is_published_to_the_exchange()
    {
        // Arrange: a spy queue bound to the exchange, created BEFORE we trigger anything.
        await using var rabbit = await fixture.ConnectToRabbitAsync();
        var spyQueue = await rabbit.BindSpyQueueAsync();

        var externalId = $"it-reader-{Guid.NewGuid():N}";

        // Act: push a notification into the fake external system (as the 3rd party would).
        var response = await fixture.ExternalApiClient().PostAsJsonAsync("/notifications", new
        {
            id = externalId,
            type = "ORDER_SHIPPED",
            customerEmail = "Anna@Example.com",
            message = "Parcel left the warehouse"
        });
        response.EnsureSuccessStatusCode();

        // Assert: within a few poll cycles the reader publishes it.
        var message = await rabbit.WaitForMessageAsync(spyQueue, m => m.MessageId == externalId, Timeout);

        Assert.NotNull(message);
        var body = message.BodyAs<RawNotification>();
        Assert.NotNull(body);
        Assert.Equal(externalId, body.Id);
        Assert.Equal("ORDER_SHIPPED", body.Type);
        Assert.Equal("Anna@Example.com", body.CustomerEmail);   // the READER does not transform — it forwards as-is
        Assert.Equal("Parcel left the warehouse", body.Message);
        Assert.Equal("external-api", body.Source);
    }

    [Fact]
    public async Task Reader_reports_its_progress_on_stats_endpoint()
    {
        var externalId = $"it-reader-stats-{Guid.NewGuid():N}";
        await fixture.ExternalApiClient().PostAsJsonAsync("/notifications", new
        {
            id = externalId, type = "PROMO", customerEmail = "mia@example.com", message = "hi"
        });

        // The reader must have polled at least once and published at least one message.
        await Wait.UntilTrueAsync(async () =>
        {
            var stats = await fixture.NotificationReaderClient().GetFromJsonAsync<ReaderStatsDto>("/stats");
            return stats is { Polls: > 0, Published: > 0 };
        }, Timeout, "reader /stats to show polls > 0 and published > 0");
    }

    private sealed record ReaderStatsDto(long Polls, long Published, long LastSequence);
}
