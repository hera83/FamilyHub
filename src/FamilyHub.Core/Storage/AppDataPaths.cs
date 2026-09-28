using FamilyHub.Core.Configuration;
using Microsoft.Extensions.Options;

namespace FamilyHub.Core.Storage;

/// <summary>
/// Where Family Hub keeps its data. Modules get their own sub-folder:
/// <c>paths.GetDirectory("madplan")</c>.
/// </summary>
public interface IAppDataPaths
{
    /// <summary>Root folder, e.g. <c>/data</c> in the Docker container.</summary>
    string Root { get; }

    /// <summary>Full path to a file under <see cref="Root"/>. Parent folders are created.</summary>
    string GetFilePath(string relativePath);

    /// <summary>Full path to a folder under <see cref="Root"/>, created if missing.</summary>
    string GetDirectory(string relativePath);
}

internal sealed class AppDataPaths : IAppDataPaths
{
    public AppDataPaths(IOptions<FamilyHubOptions> options)
    {
        var configured = options.Value.DataDirectory;
        Root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "FamilyHub")
            : Path.GetFullPath(configured);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string GetFilePath(string relativePath)
    {
        var full = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    public string GetDirectory(string relativePath)
    {
        var full = Resolve(relativePath);
        Directory.CreateDirectory(full);
        return full;
    }

    private string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        var rootWithSeparator = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Stien '{relativePath}' peger uden for datamappen.", nameof(relativePath));
        }

        return full;
    }
}
