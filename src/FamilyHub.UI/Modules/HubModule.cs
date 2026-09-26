using FamilyHub.UI.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.UI.Modules;

/// <summary>
/// Describes one menu in Family Hub (Kalender, Madplan, …).
/// Each module is its own project with one subclass of this, registered in Program.cs:
/// <code>modules.Add&lt;CalendarModule&gt;()</code>
/// See docs/standarder/nyt-modul.md.
/// </summary>
public abstract class HubModule
{
    /// <summary>Stable technical id: lower case letters, digits and dashes, e.g. "madplan". Used for data folders.</summary>
    public abstract string Id { get; }

    /// <summary>Name in the navigation – one short word, e.g. "Madplan".</summary>
    public abstract string Title { get; }

    /// <summary>One line for shortcuts on the home screen, e.g. "Ugens retter og indkøb".</summary>
    public virtual string? Description => null;

    public abstract IconName Icon { get; }

    /// <summary>The module's start page, e.g. "/madplan". Must match the page's route.</summary>
    public abstract string Route { get; }

    /// <summary>Position in the navigation – lower comes first. Use steps of 10.</summary>
    public virtual int Order => 100;

    public virtual bool ShowInNavigation => true;

    /// <summary>Components shown on the home screen overview.</summary>
    public virtual IReadOnlyList<DashboardWidget> Widgets => [];

    /// <summary>Register the module's own services (data access, sync jobs, …).</summary>
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
