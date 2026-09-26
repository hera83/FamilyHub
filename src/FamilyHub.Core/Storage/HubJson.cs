using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamilyHub.Core.Storage;

/// <summary>Shared JSON settings for files on disk and values in the browser.</summary>
public static class HubJson
{
    /// <summary>Readable files on disk: camelCase, enums as text, tolerant of hand edits.</summary>
    public static JsonSerializerOptions Files { get; } = Create(indented: true);

    /// <summary>Compact variant for localStorage and interop.</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
