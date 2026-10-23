using Somning.Core.Recording;
using Xunit;

namespace Somning.Core.Tests.Recording;

public sealed class RetentionPolicyTests
{
    [Fact]
    public void Deletes_only_delivered_records_older_than_newest_twenty()
    {
        var records = Enumerable.Range(0, 25)
            .Select(index => Sidecar($"id-{index:D2}", index, RecordingStage.Pasted))
            .ToArray();

        var delete = RetentionPolicy.SelectForDeletion(records, keep: 20);

        Assert.Equal(new[] { "id-00", "id-01", "id-02", "id-03", "id-04" }, delete);
    }

    [Fact]
    public void Keeps_recovered_in_newest_twenty_and_only_deletes_delivered_older_records()
    {
        var records = Enumerable.Range(0, 24)
            .Select(index => Sidecar($"id-{index:D2}", index,
                index is 22 or 23 ? RecordingStage.Recovered : RecordingStage.Pasted))
            .ToArray();

        var delete = RetentionPolicy.SelectForDeletion(records, keep: 20);

        Assert.Equal(new[] { "id-00", "id-01", "id-02", "id-03" }, delete);
    }

    [Fact]
    public void Never_deletes_undelivered_speech_even_when_old_or_exhausted()
    {
        var records = new[]
        {
            Sidecar("captured", 0, RecordingStage.Captured, attempts: 5),
            Sidecar("transcribed", 1, RecordingStage.Transcribed, attempts: 4),
            Sidecar("old-failed", 2, RecordingStage.Failed),
            Sidecar("new-1", 3, RecordingStage.Pasted),
            Sidecar("new-2", 4, RecordingStage.Pasted)
        };

        Assert.Equal(new[] { "old-failed" }, RetentionPolicy.SelectForDeletion(records, keep: 2));
    }

    [Fact]
    public void Keeps_all_records_when_total_does_not_exceed_limit()
    {
        var records = Enumerable.Range(0, 19).Select(index => Sidecar($"id-{index}", index, RecordingStage.Pasted));

        Assert.Empty(RetentionPolicy.SelectForDeletion(records, keep: 20));
    }

    private static RecordingSidecar Sidecar(string id, int age, RecordingStage stage, int attempts = 0) =>
        new(id, DateTimeOffset.UnixEpoch.AddMinutes(age), stage, "harness", null, null, null, attempts, null);
}
