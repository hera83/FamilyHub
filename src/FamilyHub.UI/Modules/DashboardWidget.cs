using Microsoft.AspNetCore.Components;

namespace FamilyHub.UI.Modules;

/// <summary>How much room a widget takes on the home screen grid.</summary>
public enum WidgetSize
{
    /// <summary>One column – a number, a status, the next event.</summary>
    Small,

    /// <summary>One column, room for a short list (default).</summary>
    Medium,

    /// <summary>Two columns – e.g. the week's meal plan.</summary>
    Large,

    /// <summary>Full width.</summary>
    Full,
}

/// <summary>A component a module contributes to the home screen.</summary>
public sealed record DashboardWidget
{
    public DashboardWidget(Type component, WidgetSize size = WidgetSize.Medium, int order = 100)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (!typeof(IComponent).IsAssignableFrom(component))
        {
            throw new ArgumentException($"{component.Name} er ikke en Blazor-komponent.", nameof(component));
        }

        Component = component;
        Size = size;
        Order = order;
    }

    public Type Component { get; }

    public WidgetSize Size { get; }

    /// <summary>
    /// Position among the module's own widgets – lower comes first. The home screen shows the modules
    /// in menu order (<see cref="HubModule.Order"/>), so this only matters when a module has several widgets.
    /// </summary>
    public int Order { get; }

    public static DashboardWidget For<TComponent>(WidgetSize size = WidgetSize.Medium, int order = 100)
        where TComponent : IComponent => new(typeof(TComponent), size, order);
}

/// <summary>A widget together with the module that owns it.</summary>
public sealed record ModuleWidget(HubModule Module, DashboardWidget Widget);
