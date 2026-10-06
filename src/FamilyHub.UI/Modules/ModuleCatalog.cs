using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.UI.Modules;

/// <summary>All registered modules, in navigation order. Singleton.</summary>
public sealed class ModuleCatalog
{
    internal ModuleCatalog(IEnumerable<HubModule> modules)
    {
        Modules = [.. modules
            .OrderBy(m => m.Order)
            .ThenBy(m => m.Title, StringComparer.Create(CultureInfo.GetCultureInfo("da-DK"), ignoreCase: true))];
        NavigationModules = [.. Modules.Where(m => m.ShowInNavigation)];
        // The home screen follows the menu: module by module, then each module's own widget order.
        Widgets = [.. Modules.SelectMany(m => m.Widgets
            .Select(w => new ModuleWidget(m, w))
            .OrderBy(w => w.Widget.Order))];
        ScreenSaverItems = [.. Modules
            .SelectMany(m => m.ScreenSaverItems)
            .OrderBy(i => i.Order)];
        Assemblies = [.. Modules.Select(m => m.GetType().Assembly).Distinct()];
    }

    public static ModuleCatalog Empty { get; } = new([]);

    public IReadOnlyList<HubModule> Modules { get; }

    public IReadOnlyList<HubModule> NavigationModules { get; }

    public IReadOnlyList<ModuleWidget> Widgets { get; }

    /// <summary>What the modules show beside the clock on the screen saver, in order.</summary>
    public IReadOnlyList<ScreenSaverItem> ScreenSaverItems { get; }

    /// <summary>Assemblies with routable pages – handed to the Blazor router.</summary>
    public IReadOnlyList<Assembly> Assemblies { get; }

    public HubModule? Find(string id) => Modules.FirstOrDefault(m => m.Id == id);
}

/// <summary>Collects modules in Program.cs and validates them early.</summary>
public sealed partial class ModuleCatalogBuilder
{
    // Routes that belong to the shell itself.
    private static readonly string[] ReservedRoutes = ["/indstillinger", "/not-found", "/error", "/health"];

    private readonly List<HubModule> modules = [];

    public ModuleCatalogBuilder Add<TModule>()
        where TModule : HubModule, new() => Add(new TModule());

    public ModuleCatalogBuilder Add(HubModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        Validate(module);

        if (modules.Any(m => m.Id == module.Id))
        {
            throw new InvalidOperationException($"Modul-id '{module.Id}' er registreret to gange.");
        }

        if (modules.Any(m => string.Equals(m.Route, module.Route, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Ruten '{module.Route}' bruges allerede af et andet modul.");
        }

        modules.Add(module);
        return this;
    }

    internal ModuleCatalog Build() => new(modules);

    private static void Validate(HubModule module)
    {
        var name = module.GetType().Name;

        if (string.IsNullOrWhiteSpace(module.Id) || !IdPattern().IsMatch(module.Id))
        {
            throw new InvalidOperationException($"{name}: Id '{module.Id}' må kun indeholde små bogstaver, tal og bindestreg (fx 'madplan').");
        }

        if (string.IsNullOrWhiteSpace(module.Title))
        {
            throw new InvalidOperationException($"{name}: Title mangler.");
        }

        if (string.IsNullOrWhiteSpace(module.Route) || !module.Route.StartsWith('/') || module.Route.Length < 2)
        {
            throw new InvalidOperationException($"{name}: Route '{module.Route}' skal starte med '/' og må ikke være forsiden.");
        }

        if (ReservedRoutes.Any(r => module.Route.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{name}: Route '{module.Route}' er reserveret af Family Hub.");
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex IdPattern();
}

public static class ModuleServiceCollectionExtensions
{
    /// <summary>
    /// Registers the modules (menus) and their services. Returns the catalog so the host can
    /// hand the module assemblies to the router.
    /// </summary>
    public static ModuleCatalog AddFamilyHubModules(this IServiceCollection services, IConfiguration configuration, Action<ModuleCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ModuleCatalogBuilder();
        configure(builder);
        var catalog = builder.Build();

        foreach (var module in catalog.Modules)
        {
            module.ConfigureServices(services, configuration);
        }

        services.AddSingleton(catalog);
        return catalog;
    }
}
