using System.Diagnostics;
using System.Text.Json;
using AspireNotifications.Contracts;
using RabbitMQ.Client;

namespace AspireNotifications.NotificationReader;

/// <summary>
/// Puts ONE RawNotification on the RabbitMQ exchange.
///
///   RawNotification ──JSON──► [exchange "notifications"] ──► (every bound queue)
///
/// Vocabulary:
///   Connection = the TCP link to RabbitMQ (expensive, one per app, Aspire gives it to us).
///   Channel    = a lightweight "session" inside the connection. You do all work on a channel.
///                A channel is NOT thread-safe, so we guard it with a semaphore.
/// </summary>
public sealed class NotificationPublisher(IConnection connection, ILogger<NotificationPublisher> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(RawNotification notification, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);

            // A span for the dashboard. Kind=Producer is the OpenTelemetry word for "I sent a message".
            using var activity = Telemetry.Source.StartActivity("notifications publish", ActivityKind.Producer);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", Messaging.Exchange);
            activity?.SetTag("notification.id", notification.Id);
            activity?.SetTag("notification.type", notification.Type);

            var body = JsonSerializer.SerializeToUtf8Bytes(notification);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                MessageId = notification.Id,   // lets the consumer and the tests identify the message
                Persistent = true,             // write to disk → survives a RabbitMQ restart (needs a durable queue too)
                Headers = new Dictionary<string, object?>()
            };

            // Carry the trace id with the message. The consumer reads it back, so the dashboard
            // shows ONE trace: reader → RabbitMQ → customer-interface → Postgres.
            if (activity?.Id is { } traceParent)
            {
                properties.Headers[Messaging.TraceParentHeader] = traceParent;
            }

            await channel.BasicPublishAsync(
                exchange: Messaging.Exchange,
                routingKey: string.Empty,      // fanout exchanges ignore the routing key
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: ct);

            logger.LogInformation("Published notification {Id} ({Type}) to exchange '{Exchange}'",
                notification.Id, notification.Type, Messaging.Exchange);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        _channel = await connection.CreateChannelAsync(cancellationToken: ct);

        // "Make sure the exchange/queues exist" — safe to call every time (see RabbitTopology).
        await RabbitTopology.DeclareAsync(_channel, ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.CloseAsync();
            _channel.Dispose();
        }
        _gate.Dispose();
    }
}
