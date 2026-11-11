namespace AspireNotifications.FakeExternalApi;

/// <summary>What the external API returns. Note the Sequence: a growing number used as the polling cursor.</summary>
public sealed record ExternalNotification(
    long Sequence,
    string Id,
    string Type,
    string CustomerEmail,
    string Message,
    DateTimeOffset OccurredAt);

/// <summary>
/// In-memory "database" of the fake external system.
/// Pure C#, no I/O → easy to unit test (see UnitTests/NotificationStoreTests).
/// </summary>
public sealed class NotificationStore
{
    private readonly List<ExternalNotification> _items = [];
    private readonly Lock _lock = new();
    private long _sequence;

    public ExternalNotification Add(string type, string customerEmail, string message, string? id = null)
    {
        lock (_lock)
        {
            _sequence++;
            var item = new ExternalNotification(
                Sequence: _sequence,
                Id: id ?? Guid.NewGuid().ToString("N"),
                Type: type,
                CustomerEmail: customerEmail,
                Message: message,
                OccurredAt: DateTimeOffset.UtcNow);
            _items.Add(item);
            return item;
        }
    }

    /// <summary>Everything with a Sequence greater than the one the caller already has.</summary>
    public IReadOnlyList<ExternalNotification> GetAfter(long afterSequence)
    {
        lock (_lock)
        {
            return _items.Where(n => n.Sequence > afterSequence).ToList();
        }
    }

    public int Count
    {
        get { lock (_lock) { return _items.Count; } }
    }
}
