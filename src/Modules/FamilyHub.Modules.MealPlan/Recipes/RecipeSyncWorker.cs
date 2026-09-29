using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Modules.MealPlan.Recipes;

/// <summary>
/// Keeps the local copy of the recipe book fresh: fetches at start-up and every <see cref="RecipeService.MaxAge"/>,
/// sooner again after a failure. Screens never wait for it – they show the copy and get <see cref="RecipeService.Changed"/>.
/// </summary>
internal sealed class RecipeSyncWorker(RecipeService recipes, TimeProvider time, ILogger<RecipeSyncWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var ok = false;
            try
            {
                // Not configured = nothing to fetch; check again later in case the key has been added.
                ok = !recipes.IsConfigured || await recipes.RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the worker die – the next round may well succeed. Status is shown on the screen.
                logger.LogError(ex, "Refreshing the recipe book failed");
            }

            try
            {
                await Task.Delay(ok ? RecipeService.MaxAge : RetryAfterFailure, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
