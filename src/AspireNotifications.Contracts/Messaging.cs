namespace AspireNotifications.Contracts;

/// <summary>
/// All the RabbitMQ NAMES in one place, so nobody mistypes "notifcations".
///
/// RabbitMQ in one picture:
///
///   Publisher ──► EXCHANGE ──(binding)──► QUEUE ──► Consumer
///
///   * A publisher NEVER sends to a queue directly. It sends to an EXCHANGE.
///   * The exchange decides which QUEUES get a copy. We use a FANOUT exchange:
///     "every queue bound to me gets a copy". Simplest kind.
///   * A consumer reads from ONE queue. Each consumer-service has its own queue,
///     so two different services can both receive the same message.
///   * A message that a consumer rejects goes to the DEAD-LETTER exchange/queue
///     instead of disappearing. Think of it as the "failed messages" tray.
/// </summary>
public static class Messaging
{
    /// <summary>Name Aspire gives the RabbitMQ resource. Services use it to find the connection string.</summary>
    public const string RabbitConnectionName = "rabbitmq";

    /// <summary>The fanout exchange the NotificationReader publishes to.</summary>
    public const string Exchange = "notifications";

    /// <summary>The queue the CustomerInterface reads from (bound to the exchange above).</summary>
    public const string CustomerInterfaceQueue = "customer-interface.notifications";

    /// <summary>Exchange + queue where rejected ("poison") messages end up.</summary>
    public const string DeadLetterExchange = "notifications.dead";
    public const string DeadLetterQueue = "notifications.dead";

    /// <summary>Message header we use to carry the OpenTelemetry trace id across the queue.</summary>
    public const string TraceParentHeader = "traceparent";
}
