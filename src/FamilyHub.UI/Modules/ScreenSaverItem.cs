using Microsoft.AspNetCore.Components;

namespace FamilyHub.UI.Modules;

/// <summary>
/// A small extra value a module shows on the screen saver, in a ring beside the clock (e.g. the glucose).
/// The screen saver draws the ring and moves it; the component only draws what goes inside – quiet, in the
/// screen saver's dim colours (--hub-saver-text …), sized in em. It renders nothing when it has nothing to show,
/// and then the ring stays away too.
/// </summary>
public sealed record ScreenSaverItem
{
    public ScreenSaverItem(Type component, int order = 100)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!typeof(IComponent).IsAssignableFrom(component))
        {
            throw new ArgumentException($"{component.Name} er ikke en Blazor-komponent.", nameof(component));
        }

        Component = component;
        Order = order;
    }

    public Type Component { get; }

    /// <summary>Order when several modules show something – lower comes first.</summary>
    public int Order { get; }

    public static ScreenSaverItem For<TComponent>(int order = 100)
        where TComponent : IComponent => new(typeof(TComponent), order);
}
