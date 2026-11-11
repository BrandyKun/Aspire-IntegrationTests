namespace AspireNotifications.CustomerInterface.Data;

public enum Severity
{
    Low,
    Info,
    High,
    Critical
}

/// <summary>
/// THE DATABASE ROW. This is the "transformed" version of a RawNotification:
/// a friendly title, a severity, normalised email. This is what a customer-facing UI would show.
/// </summary>
public sealed class CustomerNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The Id from the external system. UNIQUE → a duplicate message can't create a second row.</summary>
    public required string ExternalId { get; set; }

    public required string CustomerEmail { get; set; }

    public required string Title { get; set; }

    public required string Body { get; set; }

    public Severity Severity { get; set; }

    /// <summary>The original type code (ORDER_SHIPPED…), kept for filtering/debugging.</summary>
    public required string SourceType { get; set; }

    /// <summary>When it happened in the external system.</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>When WE processed it.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
