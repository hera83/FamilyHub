using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.School.Feeds;

/// <summary>Looks every 5 minutes for calendar links that are due (every 30 minutes, 10 after a failure) – and new ones at once.</summary>
internal sealed class ScheduleFeedWorker(ScheduleFeedService feeds, TimeProvider time, ILogger<ScheduleFeedWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await feeds.RefreshDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the worker die – the next round may well succeed. Status is shown on the screen.
                logger.LogError(ex, "School calendar refresh failed");
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
