using FamilyHub.UI.Components;
using FamilyHub.UI.Modules;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyHub.Tests.UI;

public class ModuleCatalogTests
{
    private sealed class TestModule(string id, string title, string route, int order = 100, bool showInNavigation = true, IReadOnlyList<DashboardWidget>? widgets = null) : HubModule
    {
        public bool ServicesConfigured { get; private set; }

        public override string Id => id;

        public override string Title => title;

        public override IconName Icon => IconName.Star;

        public override string Route => route;

        public override int Order => order;

        public override bool ShowInNavigation => showInNavigation;

        public override IReadOnlyList<DashboardWidget> Widgets => widgets ?? [];

        public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) => ServicesConfigured = true;
    }

    private sealed class WidgetA : ComponentBase;

    private sealed class WidgetB : ComponentBase;

    private static ModuleCatalog Build(params HubModule[] modules)
    {
        var builder = new ModuleCatalogBuilder();
        foreach (var module in modules)
        {
            builder.Add(module);
        }

        return builder.Build();
    }

    [Fact]
    public void Modules_are_ordered_by_order_then_title()
    {
        var catalog = Build(
            new TestModule("madplan", "Madplan", "/madplan", 20),
            new TestModule("opgaver", "Opgaver", "/opgaver", 20),
            new TestModule("kalender", "Kalender", "/kalender", 10),
            new TestModule("aeble", "Æbler", "/aebler", 20));

        Assert.Equal(["Kalender", "Madplan", "Opgaver", "Æbler"], catalog.Modules.Select(m => m.Title));
    }

    [Fact]
    public void Hidden_modules_are_not_in_the_navigation()
    {
        var catalog = Build(new TestModule("synlig", "Synlig", "/synlig"), new TestModule("skjult", "Skjult", "/skjult", showInNavigation: false));

        Assert.Equal(["Synlig"], catalog.NavigationModules.Select(m => m.Title));
        Assert.Equal(2, catalog.Modules.Count);
    }

    [Fact]
    public void Widgets_from_all_modules_are_collected_in_order()
    {
        var catalog = Build(
            new TestModule("a", "A", "/a", widgets: [DashboardWidget.For<WidgetA>(order: 20)]),
            new TestModule("b", "B", "/b", widgets: [DashboardWidget.For<WidgetB>(WidgetSize.Large, order: 10)]));

        Assert.Equal([typeof(WidgetB), typeof(WidgetA)], catalog.Widgets.Select(w => w.Widget.Component));
        Assert.Equal("b", catalog.Widgets[0].Module.Id);
    }

    [Theory]
    [InlineData("Madplan", "/madplan")]  // upper case id
    [InlineData("mad plan", "/madplan")] // space
    [InlineData("madplan", "madplan")]   // route without slash
    [InlineData("madplan", "/")]         // the home page
    [InlineData("madplan", "/indstillinger/madplan")] // reserved
    public void Invalid_modules_are_rejected_at_startup(string id, string route)
    {
        Assert.Throws<InvalidOperationException>(() => Build(new TestModule(id, "Titel", route)));
    }

    [Fact]
    public void Duplicate_ids_and_routes_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Build(new TestModule("x", "X", "/x"), new TestModule("x", "Y", "/y")));
        Assert.Throws<InvalidOperationException>(() => Build(new TestModule("x", "X", "/x"), new TestModule("y", "Y", "/X")));
    }

    [Fact]
    public void Registering_modules_configures_their_services_and_the_catalog()
    {
        var module = new TestModule("kalender", "Kalender", "/kalender");
        var services = new ServiceCollection();

        var catalog = services.AddFamilyHubModules(new ConfigurationBuilder().Build(), m => m.Add(module));

        Assert.True(module.ServicesConfigured);
        Assert.Same(catalog, services.BuildServiceProvider().GetRequiredService<ModuleCatalog>());
        Assert.Contains(typeof(ModuleCatalogTests).Assembly, catalog.Assemblies);
    }

    [Fact]
    public void Widgets_must_be_components()
    {
        Assert.Throws<ArgumentException>(() => new DashboardWidget(typeof(string)));
    }
}
