using System.Diagnostics;
using System.Text.Json;
using Somning.Core.Paths;

namespace Somning.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonSettingsStore() : this(Path.Combine(SomningPaths.DataRoot, "settings.json")) { }

    public JsonSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
    }

    public string FilePath { get; }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
            return new UserSettings();

        try
        {
            await using var stream = new FileStream(
                FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<UserSettings>(stream, SerializerOptions, cancellationToken)
                ?? new UserSettings();
        }
        catch (JsonException ex)
        {
            ArchiveCorruptFile(ex.Message);
            return new UserSettings();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = FilePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, FilePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private void ArchiveCorruptFile(string reason)
    {
        var archivedPath = $"{FilePath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            File.Move(FilePath, archivedPath);
            Trace.TraceWarning(
                "Somning settings file '{0}' was corrupt ({1}); archived to '{2}'. Defaults loaded.",
                FilePath, reason, archivedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(
                "Somning settings file '{0}' was corrupt ({1}) and could not be archived: {2}. Defaults loaded.",
                FilePath, reason, ex.Message);
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not remove temporary settings file '{0}': {1}", tempPath, ex.Message);
        }
    }
}
