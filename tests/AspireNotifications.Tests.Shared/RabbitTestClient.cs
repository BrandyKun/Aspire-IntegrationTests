using System.Text;
using System.Text.Json;
using AspireNotifications.Contracts;
using RabbitMQ.Client;

namespace AspireNotifications.Tests.Shared;

/// <summary>
/// Lets a test act as (a) a PUBLISHER, to inject a message without going through the reader,
/// and (b) a spy CONSUMER, to see what the reader published.
///
/// For (b) we declare our OWN temporary queue bound to the fanout exchange. Because the
/// exchange copies every message to every bound queue, we see the same messages the
/// CustomerInterface sees, without stealing them from it. That's the whole point of
/// publishing to an exchange instead of straight to a queue.
/// </summary>
public sealed class RabbitTestClient : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitTestClient(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitTestClient> ConnectAsync(string connectionString)
    {
        var factory = new ConnectionFactory { Uri = new Uri(connectionString) };
        var connection = await factory.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await RabbitTopology.DeclareAsync(channel);
        return new RabbitTestClient(connection, channel);
    }

    /// <summary>Create a private, server-named queue that receives a copy of everything on the main exchange.</summary>
    public async Task<string> BindSpyQueueAsync()
    {
        // No name, exclusive, auto-delete → RabbitMQ names it (amq.gen-xxxx) and removes it when we disconnect.
        var ok = await _channel.QueueDeclareAsync();
        await _channel.QueueBindAsync(ok.QueueName, Messaging.Exchange, routingKey: string.Empty);
        return ok.QueueName;
    }

    /// <summary>Publish a message exactly the way the NotificationReader does.</summary>
    public Task PublishAsync(RawNotification notification) =>
        PublishRawAsync(JsonSerializer.SerializeToUtf8Bytes(notification), notification.Id);

    /// <summary>Publish ANY bytes (used to send a deliberately broken message).</summary>
    public async Task PublishRawAsync(byte[] body, string? messageId = null)
    {
        var props = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = messageId,
            Persistent = true
        };

        await _channel.BasicPublishAsync(
            exchange: Messaging.Exchange,
            routingKey: string.Empty,
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    /// <summary>
    /// Pull messages from a queue until one satisfies <paramref name="match"/> or we time out.
    /// Returns the matching message, or null. Non-matching messages are consumed (acked) and skipped.
    /// </summary>
    public async Task<ReceivedMessage?> WaitForMessageAsync(string queue, Func<ReceivedMessage, bool> match, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await _channel.BasicGetAsync(queue, autoAck: true);
            if (result is null)
            {
                await Task.Delay(250);
                continue;
            }

            var message = new ReceivedMessage(
                MessageId: result.BasicProperties.MessageId,
                Body: Encoding.UTF8.GetString(result.Body.Span));

            if (match(message))
            {
                return message;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        _channel.Dispose();
        await _connection.CloseAsync();
        _connection.Dispose();
    }
}

public sealed record ReceivedMessage(string? MessageId, string Body)
{
    public T? BodyAs<T>() => JsonSerializer.Deserialize<T>(Body);
}
