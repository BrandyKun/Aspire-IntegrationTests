namespace AspireNotifications.Contracts;

/// <summary>
/// THE MESSAGE. This is exactly what the NotificationReader puts on RabbitMQ
/// and exactly what the CustomerInterface takes off RabbitMQ.
///
/// It is serialised to JSON, so keep it simple: strings, numbers, dates.
/// If you ever change this shape, BOTH services must be updated (that is why it lives in Contracts).
/// </summary>
/// <param name="Id">Unique id from the external system. Used to detect duplicates.</param>
/// <param name="Type">A code like ORDER_SHIPPED or PAYMENT_FAILED. The CustomerInterface turns this into a friendly title.</param>
/// <param name="CustomerEmail">Who the notification is for.</param>
/// <param name="Message">Free text from the external system.</param>
/// <param name="OccurredAt">When it happened in the external system.</param>
/// <param name="Source">Where it came from (handy when you add a second reader later).</param>
public sealed record RawNotification(
    string Id,
    string Type,
    string CustomerEmail,
    string Message,
    DateTimeOffset OccurredAt,
    string Source = "external-api");
