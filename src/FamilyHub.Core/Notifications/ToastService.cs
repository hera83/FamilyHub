using Microsoft.Extensions.Logging;

namespace FamilyHub.Core.Notifications;

/// <summary>
/// Thread-safe toast state. Scoped: each screen (Blazor circuit) has its own toasts.
/// </summary>
internal sealed class ToastService(TimeProvider time, ILogger<ToastService> logger) : IToastService, IDisposable
{
    private readonly Lock gate = new();

    // Newest first.
    private readonly List<Toast> visible = [];
    private bool disposed;

    public event Action? Changed;

    public IReadOnlyList<Toast> Visible
    {
        get
        {
            lock (gate)
            {
                return [.. visible];
            }
        }
    }

    public Toast Info(string title, string? message = null) => Show(new() { Level = ToastLevel.Info, Title = title, Message = message });

    public Toast Success(string title, string? message = null) => Show(new() { Level = ToastLevel.Success, Title = title, Message = message });

    public Toast Warning(string title, string? message = null) => Show(new() { Level = ToastLevel.Warning, Title = title, Message = message });

    public Toast Error(string title, string? message = null) => Show(new() { Level = ToastLevel.Error, Title = title, Message = message });

    public Toast Undoable(string title, Func<Task> undo, string? message = null, string actionLabel = "Fortryd")
    {
        ArgumentNullException.ThrowIfNull(undo);
        return Show(new()
        {
            Level = ToastLevel.Success,
            Title = title,
            Message = message,
            Action = new ToastAction(actionLabel, undo),
        });
    }

    public Toast Show(ToastOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Title);

        Toast toast;
        lock (gate)
        {
            if (options.Key is not null)
            {
                foreach (var existing in visible.Where(t => t.Key == options.Key).ToList())
                {
                    RemoveLocked(existing);
                }
            }

            if (FindDuplicateLocked(options) is { } duplicate)
            {
                // Same message again: count it instead of stacking copies.
                duplicate.Occurrences++;
                visible.Remove(duplicate);
                visible.Insert(0, duplicate);
                StartCountdownLocked(duplicate, duplicate.Duration);
                toast = duplicate;
            }
            else
            {
                toast = new Toast(options, ResolveDuration(options));
                visible.Insert(0, toast);
                StartCountdownLocked(toast, toast.Duration);
                TrimLocked();
            }
        }

        RaiseChanged();
        return toast;
    }

    public void Dismiss(Guid id)
    {
        bool removed;
        lock (gate)
        {
            removed = visible.Find(t => t.Id == id) is { } toast && RemoveLocked(toast);
        }

        if (removed)
        {
            RaiseChanged();
        }
    }

    public void DismissAll()
    {
        lock (gate)
        {
            if (visible.Count == 0)
            {
                return;
            }

            foreach (var toast in visible.ToList())
            {
                RemoveLocked(toast);
            }
        }

        RaiseChanged();
    }

    public void Pause(Guid id)
    {
        lock (gate)
        {
            if (visible.Find(t => t.Id == id) is not { IsPaused: false, Timer: not null } toast)
            {
                return;
            }

            toast.PausedRemaining = Max(toast.DueAt - time.GetUtcNow(), TimeSpan.Zero);
            toast.Timer.Dispose();
            toast.Timer = null;
            toast.IsPaused = true;
        }

        RaiseChanged();
    }

    public void Resume(Guid id)
    {
        lock (gate)
        {
            if (visible.Find(t => t.Id == id) is not { IsPaused: true } toast)
            {
                return;
            }

            toast.IsPaused = false;
            StartCountdownLocked(toast, Max(toast.PausedRemaining, ToastDefaults.ResumeMinimum));
        }

        RaiseChanged();
    }

    public async Task InvokeActionAsync(Guid id)
    {
        Toast? toast;
        lock (gate)
        {
            toast = visible.Find(t => t.Id == id);
        }

        if (toast?.Action is not { } action)
        {
            return;
        }

        // Close first, so a double tap cannot run the action twice.
        Dismiss(id);
        try
        {
            await action.Callback();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Toast action '{Action}' failed", action.Label);
            Error("Det lykkedes ikke", "Prøv igen om lidt.");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var toast in visible)
            {
                toast.Timer?.Dispose();
            }

            visible.Clear();
        }
    }

    private static TimeSpan? ResolveDuration(ToastOptions options)
    {
        var duration = options.Duration switch
        {
            null => ToastDefaults.For(options.Level),
            { } value when value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan => null,
            { } value => value,
        };

        if (options.Action is not null && duration is { } d && d < ToastDefaults.WithAction)
        {
            duration = ToastDefaults.WithAction;
        }

        return duration;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private Toast? FindDuplicateLocked(ToastOptions options) =>
        options.Action is null && options.Key is null
            ? visible.Find(t => t.Action is null && t.Key is null && t.Level == options.Level && t.Title == options.Title && t.Message == options.Message)
            : null;

    private void StartCountdownLocked(Toast toast, TimeSpan? remaining)
    {
        toast.Timer?.Dispose();
        toast.Timer = null;
        toast.IsPaused = false;
        toast.Generation++;

        if (remaining is not { } due || disposed)
        {
            return;
        }

        var generation = toast.Generation;
        toast.RemainingAtStart = due;
        toast.DueAt = time.GetUtcNow() + due;
        toast.Timer = time.CreateTimer(_ => Expire(toast.Id, generation), null, due, Timeout.InfiniteTimeSpan);
    }

    private void Expire(Guid id, int generation)
    {
        bool removed;
        lock (gate)
        {
            // A restarted countdown makes older timers stale.
            removed = visible.Find(t => t.Id == id) is { } toast && toast.Generation == generation && !toast.IsPaused && RemoveLocked(toast);
        }

        if (removed)
        {
            RaiseChanged();
        }
    }

    private void TrimLocked()
    {
        while (visible.Count > ToastDefaults.MaxVisible)
        {
            // Never the newest (index 0). Oldest non-error goes first; errors only when nothing else is left.
            var victim = visible.Skip(1).LastOrDefault(t => t.Level != ToastLevel.Error) ?? visible[^1];
            RemoveLocked(victim);
        }
    }

    private bool RemoveLocked(Toast toast)
    {
        toast.Timer?.Dispose();
        toast.Timer = null;
        return visible.Remove(toast);
    }

    private void RaiseChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Toast change handler failed");
        }
    }
}
