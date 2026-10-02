using FamilyHub.Core.Notifications;
using FamilyHub.Core.Printing;

namespace FamilyHub.Modules.MealPlan.Print;

/// <summary>
/// Tells a screen how the things it sent to the printer are doing: a problem once ("Printeren mangler papir" – not every
/// 3 seconds while the job is followed) and an error toast if a job fails. One per dialog; dispose it with the dialog.
/// </summary>
internal sealed class PrintJobToasts : IDisposable
{
    // Jobs printed from this screen, with the last problem told.
    private readonly Dictionary<Guid, string?> printed = [];
    private readonly PrintService printing;
    private readonly IToastService toasts;
    private readonly Func<Action, Task> invokeAsync;
    private readonly string failedTitle;
    private readonly string waitingText;

    /// <param name="invokeAsync">The component's <c>InvokeAsync</c> – job changes come from a background thread.</param>
    /// <param name="failedTitle">"Opskriften blev ikke udskrevet".</param>
    /// <param name="waitingText">Under a problem: "Opskriften udskrives, når det er ordnet."</param>
    public PrintJobToasts(PrintService printing, IToastService toasts, Func<Action, Task> invokeAsync, string failedTitle, string waitingText)
    {
        this.printing = printing;
        this.toasts = toasts;
        this.invokeAsync = invokeAsync;
        this.failedTitle = failedTitle;
        this.waitingText = waitingText;
        printing.JobChanged += OnJobChanged;
    }

    /// <summary>Follows a job this screen just sent.</summary>
    public void Follow(PrintJob job)
    {
        lock (printed)
        {
            printed[job.Id] = null;
        }
    }

    public void Dispose() => printing.JobChanged -= OnJobChanged;

    // Raised on a background thread while the job is followed.
    private void OnJobChanged(PrintJob job)
    {
        string? tell;
        lock (printed)
        {
            if (!printed.TryGetValue(job.Id, out var told))
            {
                return;
            }

            if (job.IsFinished)
            {
                printed.Remove(job.Id);
            }
            else
            {
                printed[job.Id] = job.Problem;
            }

            tell = job.Problem is { } problem && problem != told ? problem : null;
        }

        _ = invokeAsync(() =>
        {
            if (job.Status == PrintJobStatus.Failed)
            {
                toasts.Error(failedTitle, tell ?? job.Problem);
            }
            else if (tell is not null)
            {
                toasts.Warning(tell, waitingText);
            }
        });
    }
}
