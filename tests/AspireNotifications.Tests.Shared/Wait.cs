namespace AspireNotifications.Tests.Shared;

/// <summary>
/// Async systems are EVENTUALLY consistent: you send something, and a moment later it has
/// happened. So tests can't assert immediately; they poll: "is it there yet? ... yet? ... yet?"
/// until a timeout. Never use a fixed Task.Delay(5000) — it's either too slow or too flaky.
/// </summary>
public static class Wait
{
    public static async Task<T> UntilAsync<T>(Func<Task<T?>> probe, TimeSpan timeout, string what)
        where T : class
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Gave up waiting {timeout.TotalSeconds:0}s for: {what}");
            }

            await Task.Delay(250);
        }
    }

    public static async Task UntilTrueAsync(Func<Task<bool>> probe, TimeSpan timeout, string what)
    {
        await UntilAsync(async () => await probe() ? Marker : null, timeout, what);
    }

    private static readonly object Marker = new();
}
