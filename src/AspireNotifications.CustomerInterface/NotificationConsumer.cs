using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AspireNotifications.Contracts;
using AspireNotifications.CustomerInterface.Data;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AspireNotifications.CustomerInterface;

/// <summary>
/// THE LISTENER.  Sits on the queue and, for every message:
///
///   bytes ──JSON──► RawNotification ──Transform──► CustomerNotification ──EF──► Postgres
///
/// then tells RabbitMQ "done" (ACK) so the message is removed from the queue.
///
/// Why ACK matters: until we ack, RabbitMQ keeps the message. If this service crashes
/// half-way, RabbitMQ gives the message to the next consumer. That's "at-least-once
/// delivery" — and the price is that we may see the SAME message twice, hence the
/// duplicate check below.
/// </summary>
public sealed class NotificationConsumer(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ILogger<NotificationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // "Make sure exchange + queue + binding exist" (idempotent, see RabbitTopology).
        await RabbitTopology.DeclareAsync(channel, stoppingToken);

        // Prefetch = how many un-acked messages RabbitMQ may hand us at once.
        // 1 is the safest to reason about ("one at a time, in order"); raise it for throughput.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            // Rebuild the trace: start our span as a CHILD of the publisher's span (traceparent header).
            var parentId = ReadHeader(ea.BasicProperties.Headers, Messaging.TraceParentHeader);
            using var activity = Telemetry.Source.StartActivity(
                "notifications consume", ActivityKind.Consumer, parentId: parentId);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", Messaging.CustomerInterfaceQueue);
            activity?.SetTag("messaging.message.id", ea.BasicProperties.MessageId);

            try
            {
                var raw = JsonSerializer.Deserialize<RawNotification>(ea.Body.Span)
                          ?? throw new JsonException("Body deserialised to null");

                await StoreAsync(raw, stoppingToken);

                // SUCCESS → ack → RabbitMQ deletes the message from the queue.
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException)
            {
                // POISON MESSAGE: it will never succeed no matter how many times we retry.
                // requeue:false + the queue's x-dead-letter-exchange → it lands in the dead-letter queue
                // where a human can look at it. Open the RabbitMQ UI → Queues → notifications.dead.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Poison message {MessageId} sent to dead-letter queue", ea.BasicProperties.MessageId);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                // TRANSIENT problem (Postgres restarting, network blip): put it back and try again shortly.
                // In production you'd add a retry counter / delay queue to avoid a hot loop.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogWarning(ex, "Processing failed for {MessageId}, requeueing", ea.BasicProperties.MessageId);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };

        // autoAck:false = WE decide when a message is done (see Ack/Nack above).
        await channel.BasicConsumeAsync(
            queue: Messaging.CustomerInterfaceQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Listening on queue '{Queue}'", Messaging.CustomerInterfaceQueue);

        // Keep the service alive until shutdown; the consumer callback does the work.
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { /* normal shutdown */ }
    }

    private async Task StoreAsync(RawNotification raw, CancellationToken ct)
    {
        // DbContext is SCOPED (one per request), but we are a long-lived singleton,
        // so we create a small scope per message.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        // Duplicate shield #1: skip if we've already stored this external id.
        if (await db.Notifications.AnyAsync(n => n.ExternalId == raw.Id, ct))
        {
            logger.LogInformation("Duplicate {ExternalId} ignored (already stored)", raw.Id);
            return;
        }

        var row = NotificationTransformer.Transform(raw, DateTimeOffset.UtcNow);
        db.Notifications.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Stored {ExternalId} as '{Title}' ({Severity}) for {Email}",
                row.ExternalId, row.Title, row.Severity, row.CustomerEmail);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Duplicate shield #2: two copies processed at the same moment → the UNIQUE index wins.
            logger.LogInformation("Duplicate {ExternalId} ignored (unique index)", raw.Id);
        }
    }

    /// <summary>RabbitMQ delivers string headers as byte[]; turn them back into a string.</summary>
    private static string? ReadHeader(IDictionary<string, object?>? headers, string name)
    {
        if (headers is null || !headers.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => null
        };
    }
}
