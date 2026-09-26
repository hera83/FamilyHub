using Microsoft.Extensions.Logging;

namespace FamilyHub.Core.Notifications;

public static class ToastServiceExtensions
{
    /// <summary>
    /// Runs <paramref name="action"/>; if it fails, logs the exception and shows an error toast
    /// instead of breaking the page. Use for actions that are not started by a HubButton
    /// (HubButton does this by itself) – e.g. auto-save when a field is committed.
    /// </summary>
    /// <returns>True if the action succeeded.</returns>
    public static async Task<bool> TryAsync(this IToastService toasts, Func<Task> action, string errorTitle, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Action failed: {Action}", errorTitle);
            toasts.Error(errorTitle, "Prøv igen om lidt.");
            return false;
        }
    }
}
