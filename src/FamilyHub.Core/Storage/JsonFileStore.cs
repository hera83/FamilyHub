using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FamilyHub.Core.Storage;

/// <summary>
/// Stores one object as a JSON file – crash and power-cut safe.
/// Writes go to a temp file that atomically replaces the real one, so a power cut
/// mid-write leaves either the old or the new version, never half a file.
/// </summary>
public sealed class JsonFileStore<T>(string path, ILogger logger)
    where T : class
{
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public string FilePath { get; } = path;

    /// <summary>
    /// Reads the file. Missing file → null. A corrupt file is moved aside
    /// (<c>*.corrupt-yyyyMMddHHmmss</c>) so the app can start fresh, and null is returned.
    /// </summary>
    public T? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<T>(stream, HubJson.Files);
        }
        catch (JsonException ex)
        {
            var quarantine = $"{FilePath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            logger.LogError(ex, "Data file {Path} is unreadable - moved to {Quarantine}, starting fresh", FilePath, quarantine);
            try
            {
                File.Move(FilePath, quarantine, overwrite: true);
            }
            catch (IOException moveError)
            {
                logger.LogWarning(moveError, "Could not move corrupt file {Path}", FilePath);
            }

            return null;
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);

        await writeLock.WaitAsync(cancellationToken);
        try
        {
            var temp = FilePath + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, HubJson.Files, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            writeLock.Release();
        }
    }
}
