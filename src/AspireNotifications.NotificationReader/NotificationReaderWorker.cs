using System.Diagnostics;
using AspireNotifications.Contracts;

namespace AspireNotifications.NotificationReader;

/// <summary>
/// THE LOOP.  Every N seconds:
///
///   1. ask the external API "anything newer than sequence X?"
///   2. for each result → convert to RawNotification → publish to RabbitMQ
///   3. remember the highest sequence we saw (our "cursor")
///
/// It's a BackgroundService: .NET starts it when the app starts and stops it on shutdown.
/// </summary>
public sealed class NotificationReaderWorker(
    ExternalApiClient externalApi,
    NotificationPublisher publisher,
    ReaderStats stats,
    IConfiguration configuration,
    ILogger<NotificationReaderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Reader:PollIntervalSeconds", 2));
        logger.LogInformation("NotificationReader polling every {Seconds}s", interval.TotalSeconds);

        // NOTE: the cursor lives in memory. Restart the service → it starts from 0 and republishes
        // everything. That's why the CONSUMER must handle duplicates (it does, by ExternalId).
        // In a real system you'd persist this cursor (Postgres / Redis / blob).
        var lastSequence = 0L;

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // One trace per poll. The HTTP call and the publishes become child spans automatically.
            using var activity = Telemetry.Source.StartActivity("poll external api");

            try
            {
                var batch = await externalApi.GetNewerThanAsync(lastSequence, stoppingToken);
                activity?.SetTag("poll.after_sequence", lastSequence);
                activity?.SetTag("poll.received", batch.Count);

                foreach (var item in batch)
                {
                    // External shape → OUR message shape. Keep this mapping dumb and obvious.
                    var message = new RawNotification(
                        Id: item.Id,
                        Type: item.Type,
                        CustomerEmail: item.CustomerEmail,
                        Message: item.Message,
                        OccurredAt: item.OccurredAt);

                    await publisher.PublishAsync(message, stoppingToken);

                    // Only move the cursor AFTER a successful publish, so a crash mid-batch
                    // re-reads the rest next time instead of losing it.
                    lastSequence = Math.Max(lastSequence, item.Sequence);
                }

                stats.RecordPoll(lastSequence, batch.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // normal shutdown
            }
            catch (Exception ex)
            {
                // External API down? RabbitMQ hiccup? Log it and try again next tick. Don't crash the loop.
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogWarning(ex, "Poll failed, will retry in {Seconds}s", interval.TotalSeconds);
            }
        }
    }
}
