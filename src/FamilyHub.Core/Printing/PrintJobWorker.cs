using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Core.Printing;

/// <summary>
/// Follows the jobs sent from Family Hub until the print server reports them done, so <see cref="PrintService.JobChanged"/>
/// fires on every screen. Sleeps while nothing is being printed – no calls to the print server then.
/// </summary>
internal sealed class PrintJobWorker(PrintService printing, TimeProvider time, ILogger<PrintJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!printing.HasActiveJobs)
                {
                    await printing.WaitForJobsAsync(stoppingToken);
                }

                await Task.Delay(PrintService.PollInterval, time, stoppingToken);
                await printing.RefreshActiveJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the worker die – the next round may well succeed.
                logger.LogError(ex, "Following print jobs failed");
            }
        }
    }
}
