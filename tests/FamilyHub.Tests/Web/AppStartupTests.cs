using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FamilyHub.Tests.Web;

public sealed class AppStartupTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "familyhub-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(string? timeZone = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("FamilyHub:DataDirectory", dataDirectory);
            if (timeZone is not null)
            {
                builder.UseSetting("FamilyHub:TimeZone", timeZone);
            }
        });

    [Fact]
    public async Task Health_endpoint_answers_for_the_kiosk_watchdog()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetStringAsync("/health");

        Assert.Equal("Healthy", response);
    }

    [Fact]
    public async Task Privacy_policy_is_served_as_plain_html_for_google()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/privacypolicy");
        var html = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Privacy Policy", html);
        Assert.Contains("Limited Use", html);
        Assert.DoesNotContain("blazor.web", html);
    }

    [Fact]
    public async Task Start_page_serves_the_danish_app_shell()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<html lang=\"da\">", html);
        Assert.Contains("hub-boot", html);
        Assert.Contains("blazor.web", html);
        Assert.Contains("components-reconnect-modal", html);
    }

    [Fact]
    public void Unknown_time_zone_stops_startup_with_a_clear_message()
    {
        using var factory = CreateFactory(timeZone: "Mars/Olympus_Mons");

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("tidszone", error.ToString());
    }

    [Theory]
    [InlineData("/kalender")]
    [InlineData("/kalender/indstillinger")]
    public async Task Calendar_pages_are_served(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.True(response.IsSuccessStatusCode, $"{path} gav {(int)response.StatusCode}");
    }
}
