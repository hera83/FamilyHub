namespace FamilyHub.UI.Services;

public sealed record ConfirmOptions
{
    /// <summary>A question: "Slet madplanen for uge 39?"</summary>
    public required string Title { get; init; }

    /// <summary>Consequence in one or two sentences.</summary>
    public string? Message { get; init; }

    /// <summary>The verb of the action, e.g. "Slet" – never just "OK".</summary>
    public string ConfirmText { get; init; } = "OK";

    public string CancelText { get; init; } = "Annuller";

    /// <summary>Shows the confirm button in the danger colour.</summary>
    public bool Destructive { get; init; }
}

/// <summary>
/// Imperative confirmation dialogs: <c>if (await Dialogs.ConfirmAsync(...)) { ... }</c>.
/// Use sparingly – prefer doing the action and offering "Fortryd" (see IToastService.Undoable).
/// </summary>
public sealed class DialogService
{
    private TaskCompletionSource<bool>? pending;

    internal ConfirmOptions? Current { get; private set; }

    internal event Action? Changed;

    public Task<bool> ConfirmAsync(ConfirmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Only one question at a time – a new one cancels the old.
        pending?.TrySetResult(false);
        pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Current = options;
        Changed?.Invoke();
        return pending.Task;
    }

    /// <summary>Closes any open confirmation as "cancelled" – used when the screen goes idle.</summary>
    public void CloseAll() => Resolve(false);

    internal void Resolve(bool confirmed)
    {
        var completion = pending;
        if (completion is null)
        {
            return;
        }

        pending = null;
        Current = null;
        Changed?.Invoke();
        completion.TrySetResult(confirmed);
    }
}
