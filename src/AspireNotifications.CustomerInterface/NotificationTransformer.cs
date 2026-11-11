using AspireNotifications.Contracts;
using AspireNotifications.CustomerInterface.Data;

namespace AspireNotifications.CustomerInterface;

/// <summary>
/// THE BUSINESS LOGIC: RawNotification (external words) → CustomerNotification (our words).
///
/// It is a static, pure function: same input → same output, no database, no network.
/// That is exactly what makes it UNIT-testable (see UnitTests/NotificationTransformerTests).
/// Keep logic like this OUT of the consumer loop, so you can test it in milliseconds.
/// </summary>
public static class NotificationTransformer
{
    public static CustomerNotification Transform(RawNotification raw, DateTimeOffset receivedAt)
    {
        if (string.IsNullOrWhiteSpace(raw.Id))
        {
            throw new ArgumentException("Notification Id is required", nameof(raw));
        }
        if (string.IsNullOrWhiteSpace(raw.CustomerEmail))
        {
            throw new ArgumentException("CustomerEmail is required", nameof(raw));
        }

        var (title, severity) = Describe(raw.Type);

        return new CustomerNotification
        {
            ExternalId = raw.Id.Trim(),
            CustomerEmail = raw.CustomerEmail.Trim().ToLowerInvariant(), // normalise: "  Anna@Example.COM " → "anna@example.com"
            Title = title,
            Body = string.IsNullOrWhiteSpace(raw.Message) ? "(no details provided)" : raw.Message.Trim(),
            Severity = severity,
            SourceType = raw.Type,
            OccurredAt = raw.OccurredAt,
            ReceivedAt = receivedAt
        };
    }

    /// <summary>The "dictionary" from external type codes to what the customer sees.</summary>
    public static (string Title, Severity Severity) Describe(string? type) =>
        type?.Trim().ToUpperInvariant() switch
        {
            "ORDER_SHIPPED"  => ("Your order is on the way", Severity.Info),
            "PAYMENT_FAILED" => ("There was a problem with your payment", Severity.High),
            "ACCOUNT_LOCKED" => ("Your account has been locked", Severity.Critical),
            "PROMO"          => ("A new offer for you", Severity.Low),
            _                => ("You have a new notification", Severity.Low)
        };
}
