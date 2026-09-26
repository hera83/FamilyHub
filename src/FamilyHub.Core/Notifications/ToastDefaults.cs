namespace FamilyHub.Core.Notifications;

/// <summary>
/// The shared toast rules. Change them here – never per module.
/// Documented in docs/standarder/feedback.md.
/// </summary>
public static class ToastDefaults
{
    /// <summary>More than this and the oldest toast gives way (errors last).</summary>
    public const int MaxVisible = 3;

    public static readonly TimeSpan Success = TimeSpan.FromSeconds(4);

    public static readonly TimeSpan Info = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Warning = TimeSpan.FromSeconds(8);

    /// <summary>A toast with a button (e.g. "Fortryd") gets at least this long.</summary>
    public static readonly TimeSpan WithAction = TimeSpan.FromSeconds(8);

    /// <summary>After being touched/paused, a toast stays at least this long.</summary>
    public static readonly TimeSpan ResumeMinimum = TimeSpan.FromSeconds(2);

    /// <summary>Standard display time. null = stays until closed (errors).</summary>
    public static TimeSpan? For(ToastLevel level) => level switch
    {
        ToastLevel.Success => Success,
        ToastLevel.Info => Info,
        ToastLevel.Warning => Warning,
        _ => null,
    };
}
