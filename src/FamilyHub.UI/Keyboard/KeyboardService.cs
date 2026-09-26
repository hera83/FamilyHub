namespace FamilyHub.UI.Keyboard;

public enum KeyboardVisibility
{
    Hidden,
    Expanded,

    /// <summary>Collapsed to a small "Vis tastatur" button while the field keeps focus.</summary>
    Minimized,
}

/// <summary>
/// State of the on-screen keyboard for one screen. The keyboard itself decides when to
/// appear (see KeyboardPolicy and keyboard.js); pages normally never touch this.
/// </summary>
public sealed class KeyboardService
{
    public KeyboardVisibility Visibility { get; private set; }

    /// <summary>The field being typed into, or null.</summary>
    public KeyboardTarget? Target { get; private set; }

    public bool IsExpanded => Visibility == KeyboardVisibility.Expanded;

    public event Action? Changed;

    public void Minimize()
    {
        if (Target is null || Visibility == KeyboardVisibility.Minimized)
        {
            return;
        }

        Visibility = KeyboardVisibility.Minimized;
        Changed?.Invoke();
    }

    public void Expand()
    {
        if (Target is null || Visibility == KeyboardVisibility.Expanded)
        {
            return;
        }

        Visibility = KeyboardVisibility.Expanded;
        Changed?.Invoke();
    }

    internal void Attach(KeyboardTarget target)
    {
        Target = target;
        Visibility = KeyboardVisibility.Expanded;
        Changed?.Invoke();
    }

    internal void Detach()
    {
        if (Target is null && Visibility == KeyboardVisibility.Hidden)
        {
            return;
        }

        Target = null;
        Visibility = KeyboardVisibility.Hidden;
        Changed?.Invoke();
    }
}
