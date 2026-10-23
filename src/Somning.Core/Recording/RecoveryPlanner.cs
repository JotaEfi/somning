namespace Somning.Core.Recording;

public sealed record RecoveryItem(string Id, string? SavedText);

public sealed record RecoveryPlan(IReadOnlyList<RecoveryItem> Items, IReadOnlyList<string> ExhaustedIds);

public static class RecoveryPlanner
{
    public static RecoveryPlan Plan(IEnumerable<RecordingSidecar> sidecars, int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);

        var retryable = sidecars
            .Where(sidecar => sidecar.Stage is RecordingStage.Captured or RecordingStage.Transcribed)
            .OrderBy(sidecar => sidecar.CreatedUtc)
            .ThenBy(sidecar => sidecar.Id, StringComparer.Ordinal)
            .ToArray();

        var exhausted = retryable
            .Where(sidecar => sidecar.Attempts >= maxAttempts)
            .Select(sidecar => sidecar.Id)
            .ToArray();
        var items = retryable
            .Where(sidecar => sidecar.Attempts < maxAttempts)
            .Select(sidecar => new RecoveryItem(
                sidecar.Id,
                sidecar.Stage == RecordingStage.Transcribed ? sidecar.Text : null))
            .ToArray();

        return new RecoveryPlan(items, exhausted);
    }
}
