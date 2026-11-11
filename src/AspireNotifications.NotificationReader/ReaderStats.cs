namespace AspireNotifications.NotificationReader;

/// <summary>Tiny counters so GET /stats can show you what the reader has done. Thread-safe.</summary>
public sealed class ReaderStats
{
    private long _polls;
    private long _published;
    private long _lastSequence;
    private DateTimeOffset? _lastPollAt;

    public void RecordPoll(long lastSequence, int publishedNow)
    {
        Interlocked.Increment(ref _polls);
        Interlocked.Add(ref _published, publishedNow);
        Interlocked.Exchange(ref _lastSequence, lastSequence);
        _lastPollAt = DateTimeOffset.UtcNow;
    }

    public long LastSequence => Interlocked.Read(ref _lastSequence);

    public object Snapshot() => new
    {
        polls = Interlocked.Read(ref _polls),
        published = Interlocked.Read(ref _published),
        lastSequence = LastSequence,
        lastPollAt = _lastPollAt
    };
}
