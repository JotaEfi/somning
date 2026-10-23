using System.Text;
using System.Text.Json;
using Somning.Core.Recording;

namespace Somning.Platform.Windows.Recording;

public sealed class SidecarStore
{
    private static readonly object MoveLock = new();
    private readonly string recordingsDir;

    public SidecarStore(string recordingsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingsDir);
        this.recordingsDir = Path.GetFullPath(recordingsDir);
        Directory.CreateDirectory(this.recordingsDir);
    }

    public event Action<string, Exception>? CorruptFileSkipped;

    public void Save(RecordingSidecar sidecar)
    {
        ArgumentNullException.ThrowIfNull(sidecar);
        var finalPath = SidecarPath(sidecar.Id);
        var temporaryPath = Path.Combine(recordingsDir, $"{sidecar.Id}.{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(sidecar, RecordingSidecarJsonContext.Default.RecordingSidecar);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            lock (MoveLock)
                File.Move(temporaryPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public RecordingSidecar? Load(string id)
    {
        var path = SidecarPath(id);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), RecordingSidecarJsonContext.Default.RecordingSidecar);
    }

    public IReadOnlyList<RecordingSidecar> LoadAll()
    {
        var loaded = new List<RecordingSidecar>();
        foreach (var path in Directory.EnumerateFiles(recordingsDir, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                var sidecar = JsonSerializer.Deserialize(File.ReadAllText(path, Encoding.UTF8), RecordingSidecarJsonContext.Default.RecordingSidecar);
                if (sidecar is null || !IsValidId(sidecar.Id) || !string.Equals(Path.GetFileNameWithoutExtension(path), sidecar.Id, StringComparison.Ordinal))
                    throw new JsonException("Sidecar identity does not match its file name.");
                loaded.Add(sidecar);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                CorruptFileSkipped?.Invoke(Path.GetFileName(path), ex);
            }
        }
        return loaded;
    }

    public void Delete(string id)
    {
        var sidecarPath = SidecarPath(id);
        var wavPath = Path.Combine(recordingsDir, id + ".wav");
        if (File.Exists(wavPath)) File.Delete(wavPath);
        if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
    }

    private string SidecarPath(string id)
    {
        if (!IsValidId(id)) throw new ArgumentException("Recording id must be a simple file name.", nameof(id));
        return Path.Combine(recordingsDir, id + ".json");
    }

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        string.Equals(id, Path.GetFileName(id), StringComparison.Ordinal) &&
        id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !id.Contains('/') && !id.Contains('\\') && id is not "." and not "..";
}
