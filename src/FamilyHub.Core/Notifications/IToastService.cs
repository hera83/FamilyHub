namespace FamilyHub.Core.Notifications;

/// <summary>
/// The one way to show short feedback messages ("toasts"). One instance per screen/connection.
/// <para>
/// Use toasts for feedback on something the user just did. Use an <c>InfoBox</c> for a state that
/// lasts, and a dialog only when the user must decide. See docs/standarder/feedback.md.
/// </para>
/// </summary>
public interface IToastService
{
    /// <summary>Raised on any change. May be raised from a background thread.</summary>
    event Action? Changed;

    /// <summary>Visible toasts, newest first.</summary>
    IReadOnlyList<Toast> Visible { get; }

    Toast Show(ToastOptions options);

    Toast Info(string title, string? message = null);

    Toast Success(string title, string? message = null);

    Toast Warning(string title, string? message = null);

    /// <summary>Stays until closed. Say what happened and what to do: "Kunne ikke gemme – prøv igen".</summary>
    Toast Error(string title, string? message = null);

    /// <summary>
    /// The preferred pattern for deleting: do it immediately, then offer "Fortryd"
    /// instead of asking "Er du sikker?" first.
    /// </summary>
    Toast Undoable(string title, Func<Task> undo, string? message = null, string actionLabel = "Fortryd");

    void Dismiss(Guid id);

    void DismissAll();

    /// <summary>Stops the countdown while a finger rests on the toast.</summary>
    void Pause(Guid id);

    void Resume(Guid id);

    /// <summary>Runs the toast's action (e.g. undo), closes it, and reports failures as an error toast.</summary>
    Task InvokeActionAsync(Guid id);
}
