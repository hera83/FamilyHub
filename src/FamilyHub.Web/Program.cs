using System.Globalization;
using FamilyHub.Core;
using FamilyHub.Modules.Calendar;
using FamilyHub.Modules.Dexcom;
using FamilyHub.Modules.MealPlan;
using FamilyHub.Modules.School;
using FamilyHub.UI;
using FamilyHub.UI.Modules;
using FamilyHub.Web.Components;

// Danish everywhere: dates, numbers (decimal comma) and week numbers.
var danish = CultureInfo.GetCultureInfo("da-DK");
CultureInfo.DefaultThreadCurrentCulture = danish;
CultureInfo.DefaultThreadCurrentUICulture = danish;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options => options.DetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddHealthChecks();

builder.Services.AddFamilyHubCore(builder.Configuration);
builder.Services.AddFamilyHubUI();

// The menus. A new module = a new project + one line here. See docs/standarder/nyt-modul.md.
var modules = builder.Services.AddFamilyHubModules(builder.Configuration, modules => modules
    .Add<CalendarModule>()
    .Add<MealPlanModule>()
    .Add<DexcomModule>()
    .Add<SchoolModule>());

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new("da-DK"),
    SupportedCultures = [danish],
    SupportedUICultures = [danish],
});
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");

// Public privacy policy required by Google's OAuth consent screen. Plain HTML (not Blazor), so it can be read
// without JavaScript. Intentionally not linked from the app.
app.MapGet("/privacypolicy", (IWebHostEnvironment env) =>
    Results.Stream(env.WebRootFileProvider.GetFileInfo("privacypolicy.html").CreateReadStream(), "text/html; charset=utf-8"));
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies([.. modules.Assemblies.Where(a => a != typeof(Program).Assembly)]);

app.Run();

// Makes Program visible to the integration tests (WebApplicationFactory<Program>).
public partial class Program;
