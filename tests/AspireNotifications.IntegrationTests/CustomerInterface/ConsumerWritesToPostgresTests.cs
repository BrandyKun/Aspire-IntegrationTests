using System.Text;

namespace AspireNotifications.IntegrationTests.CustomerInterface;

/// <summary>
/// INTEGRATION TESTS for the CustomerInterface.
///
///   [test publishes] ──► RabbitMQ ──► customer-interface ──► Postgres ◄── [test reads SQL]
///
/// We BYPASS the reader and the external API: the test puts the message on the exchange
/// itself. That way, if this test fails, you know the problem is in the CustomerInterface
/// (or its queue/database wiring) and nowhere else.
/// </summary>
[Collection(AppHostCollection.Name)]
public class ConsumerWritesToPostgresTests(AppHostFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task Message_on_the_queue_becomes_a_transformed_row_in_postgres()
    {
        await using var rabbit = await fixture.ConnectToRabbitAsync();
        var postgres = await fixture.ConnectToPostgresAsync();
        var externalId = $"it-consumer-{Guid.NewGuid():N}";

        // Act: publish exactly what the reader would publish.
        await rabbit.PublishAsync(new RawNotification(
            Id: externalId,
            Type: "PAYMENT_FAILED",
            CustomerEmail: "  Luca@Example.COM ",
            Message: "Card declined",
            OccurredAt: DateTimeOffset.UtcNow));

        // Assert: the row appears, and it is the TRANSFORMED version.
        var row = await Wait.UntilAsync(() => postgres.FindByExternalIdAsync(externalId), Timeout,
            $"row {externalId} in customer_notifications");

        Assert.Equal("luca@example.com", row.CustomerEmail);                      // normalised
        Assert.Equal("There was a problem with your payment", row.Title);        // friendly title
        Assert.Equal("High", row.Severity);                                      // stored as text
        Assert.Equal("PAYMENT_FAILED", row.SourceType);
    }

    [Fact]
    public async Task Same_message_delivered_twice_is_stored_once()
    {
        // At-least-once delivery means duplicates WILL happen (e.g. reader restarts and re-reads).
        await using var rabbit = await fixture.ConnectToRabbitAsync();
        var postgres = await fixture.ConnectToPostgresAsync();
        var externalId = $"it-dup-{Guid.NewGuid():N}";

        var message = new RawNotification(externalId, "ORDER_SHIPPED", "anna@example.com", "x", DateTimeOffset.UtcNow);
        await rabbit.PublishAsync(message);
        await rabbit.PublishAsync(message); // ← the duplicate

        await Wait.UntilAsync(() => postgres.FindByExternalIdAsync(externalId), Timeout, $"row {externalId}");

        // Give the second copy a moment to be processed too, then prove it did NOT create a second row.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(1, await postgres.CountByExternalIdAsync(externalId));
    }

    [Fact]
    public async Task Broken_message_goes_to_the_dead_letter_queue_and_does_not_block_the_next_one()
    {
        await using var rabbit = await fixture.ConnectToRabbitAsync();
        var postgres = await fixture.ConnectToPostgresAsync();

        // A message that can never be processed: it isn't even JSON.
        var poisonId = $"it-poison-{Guid.NewGuid():N}";
        await rabbit.PublishRawAsync(Encoding.UTF8.GetBytes($"this is not json {poisonId}"), messageId: poisonId);

        // A healthy message right behind it.
        var goodId = $"it-after-poison-{Guid.NewGuid():N}";
        await rabbit.PublishAsync(new RawNotification(goodId, "PROMO", "mia@example.com", "20% off", DateTimeOffset.UtcNow));

        // 1) The poison message ends up in the dead-letter queue (not lost, not retried forever).
        var dead = await rabbit.WaitForMessageAsync(Messaging.DeadLetterQueue, m => m.MessageId == poisonId, Timeout);
        Assert.NotNull(dead);
        Assert.Contains(poisonId, dead.Body);

        // 2) The good message behind it was still processed.
        var row = await Wait.UntilAsync(() => postgres.FindByExternalIdAsync(goodId), Timeout, $"row {goodId}");
        Assert.Equal("A new offer for you", row.Title);
    }
}
