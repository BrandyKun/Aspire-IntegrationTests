using RabbitMQ.Client;

namespace AspireNotifications.Contracts;

/// <summary>
/// "Topology" = the exchanges, queues and bindings that must exist in RabbitMQ.
///
/// IMPORTANT MENTAL MODEL: Declare means "make sure this exists". It is safe to call
/// it many times (idempotent). So the publisher, the consumer AND the tests all call
/// this on start-up, and whoever is first actually creates things. Nobody has to
/// "set up RabbitMQ" by hand, and a brand-new RabbitMQ container is ready in one call.
/// </summary>
public static class RabbitTopology
{
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        // 1) Dead-letter exchange + queue. Created FIRST because the main queue points at it.
        await channel.ExchangeDeclareAsync(
            exchange: Messaging.DeadLetterExchange,
            type: ExchangeType.Fanout,
            durable: true,          // survives a RabbitMQ restart
            autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: Messaging.DeadLetterQueue,
            durable: true,          // the queue itself survives restarts
            exclusive: false,       // other connections may use it
            autoDelete: false,      // don't delete when the last consumer leaves
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: Messaging.DeadLetterQueue,
            exchange: Messaging.DeadLetterExchange,
            routingKey: string.Empty, // fanout ignores routing keys
            cancellationToken: ct);

        // 2) The main exchange the reader publishes to.
        await channel.ExchangeDeclareAsync(
            exchange: Messaging.Exchange,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // 3) The CustomerInterface's own queue, bound to the main exchange.
        //    The "x-dead-letter-exchange" argument says: "if a consumer REJECTS a message
        //    from this queue, send it to the dead-letter exchange instead of dropping it".
        await channel.QueueDeclareAsync(
            queue: Messaging.CustomerInterfaceQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = Messaging.DeadLetterExchange
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: Messaging.CustomerInterfaceQueue,
            exchange: Messaging.Exchange,
            routingKey: string.Empty,
            cancellationToken: ct);
    }
}
