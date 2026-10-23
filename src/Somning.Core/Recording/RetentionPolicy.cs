namespace Somning.Core.Recording;

public static class RetentionPolicy
{
    private static readonly HashSet<RecordingStage> DeletableStages =
    [
        RecordingStage.ClipboardSet,
        RecordingStage.Pasted,
        RecordingStage.PasteDeferred,
        RecordingStage.Empty,
        RecordingStage.Failed,
        RecordingStage.Recovered,
        RecordingStage.Cancelled
    ];

    public static IReadOnlyList<string> SelectForDeletion(IEnumerable<RecordingSidecar> sidecars, int keep = 20)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);

        var newestFirst = sidecars
            .OrderByDescending(sidecar => sidecar.CreatedUtc)
            .ThenBy(sidecar => sidecar.Id, StringComparer.Ordinal)
            .ToArray();

        return newestFirst
            .Skip(keep)
            .Where(sidecar => DeletableStages.Contains(sidecar.Stage))
            .OrderBy(sidecar => sidecar.CreatedUtc)
            .ThenBy(sidecar => sidecar.Id, StringComparer.Ordinal)
            .Select(sidecar => sidecar.Id)
            .ToArray();
    }
}

