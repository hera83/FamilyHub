using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.Calendar.Services;

/// <summary>Keeps the calendar fresh: syncs at start-up, every five minutes and whenever something asks for it.</summary>
internal sealed class CalendarSyncWorker(CalendarService calendar, ILogger<CalendarSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await calendar.SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the worker die – the next round may well succeed. Status is shown on the screen.
                logger.LogError(ex, "Calendar sync failed");
            }

            try
            {
                await calendar.WaitForNextSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
