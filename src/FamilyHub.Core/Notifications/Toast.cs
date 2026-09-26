namespace FamilyHub.Core.Notifications;

public enum ToastLevel
{
    /// <summary>Neutral information: "Kalenderen er opdateret".</summary>
    Info,

    /// <summary>Something the user did worked: "Gemt".</summary>
    Success,

    /// <summary>Worked, but with a caveat: "Gemt – men ikke synkroniseret endnu".</summary>
    Warning,

    /// <summary>Something failed. Stays until closed.</summary>
    Error,
}

/// <summary>A single button on a toast, typically "Fortryd".</summary>
public sealed record ToastAction(string Label, Func<Task> Callback);

public sealed record ToastOptions
{
    public ToastLevel Level { get; init; } = ToastLevel.Info;

    /// <summary>Short headline – ideally 2–5 words.</summary>
    public required string Title { get; init; }

    /// <summary>Optional one-line explanation.</summary>
    public string? Message { get; init; }

    /// <summary>
    /// null = the standard for the level (see <see cref="ToastDefaults"/>).
    /// <see cref="Timeout.InfiniteTimeSpan"/> or zero = stays until closed.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    public ToastAction? Action { get; init; }

    /// <summary>
    /// Toasts with the same key replace each other instead of stacking,
    /// e.g. "sync-status" or "keyboard-disabled".
    /// </summary>
    public string? Key { get; init; }
}

/// <summary>A toast currently on screen. Created by <see cref="IToastService"/>.</summary>
public sealed class Toast
{
    internal Toast(ToastOptions options, TimeSpan? duration)
    {
        Level = options.Level;
        Title = options.Title;
        Message = options.Message;
        Action = options.Action;
        Key = options.Key;
        Duration = duration;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public ToastLevel Level { get; }

    public string Title { get; }

    public string? Message { get; }

    public ToastAction? Action { get; }

    public string? Key { get; }

    /// <summary>Total display time. null = stays until closed.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>How many times the same message was shown in a row ("×3").</summary>
    public int Occurrences { get; internal set; } = 1;

    public bool IsPaused { get; internal set; }

    /// <summary>Changes whenever the countdown (re)starts – lets the UI restart its progress bar.</summary>
    public int Generation { get; internal set; }

    /// <summary>Time left when the current countdown started.</summary>
    public TimeSpan RemainingAtStart { get; internal set; }

    internal ITimer? Timer { get; set; }

    internal DateTimeOffset DueAt { get; set; }

    internal TimeSpan PausedRemaining { get; set; }
}
