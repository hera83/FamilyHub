using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.Dexcom.Glucose;

/// <summary>Fetches new readings once a minute (Dexcom measures every 5 minutes, so a new one shows within a minute).</summary>
internal sealed class GlucoseSyncWorker(GlucoseMonitor monitor, TimeProvider time, ILogger<GlucoseSyncWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitor.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the worker die – the next round may well succeed. Status is shown on the screen.
                logger.LogError(ex, "Glucose refresh failed");
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
