using System.ComponentModel.DataAnnotations;
using FamilyHub.Core.Devices;

namespace FamilyHub.Core.Configuration;

/// <summary>
/// Root configuration for Family Hub – the <c>"FamilyHub"</c> section in appsettings.json.
/// Validated at startup, so a typo fails fast instead of surfacing later on the kitchen screen.
/// </summary>
public sealed class FamilyHubOptions
{
    public const string SectionName = "FamilyHub";

    /// <summary>IANA time zone used for the clock, greetings and night mode.</summary>
    [Required]
    public string TimeZone { get; set; } = "Europe/Copenhagen";

    /// <summary>
    /// Folder for data files (settings, databases). Empty = the OS default
    /// (<c>%LOCALAPPDATA%\FamilyHub</c> on Windows, <c>~/.local/share/FamilyHub</c> on Linux).
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>
    /// Starting point for a screen that has never been configured.
    /// Each screen can override these under Indstillinger → Denne skærm.
    /// </summary>
    public DeviceSettings DeviceDefaults { get; set; } = new();
}
