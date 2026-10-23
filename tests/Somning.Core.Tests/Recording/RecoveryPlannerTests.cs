using Somning.Core.Recording;
using Xunit;

namespace Somning.Core.Tests.Recording;

public sealed class RecoveryPlannerTests
{
    [Fact]
    public void Plans_only_retryable_captured_or_transcribed_records_oldest_first()
    {
        var now = DateTimeOffset.UtcNow;
        var records = new[]
        {
            Sidecar("done", now.AddMinutes(2), RecordingStage.Pasted, attempts: 0),
            Sidecar("text", now.AddMinutes(1), RecordingStage.Transcribed, text: "saved", attempts: 2),
            Sidecar("capture", now, RecordingStage.Captured, attempts: 0),
            Sidecar("empty", now.AddMinutes(3), RecordingStage.Empty, attempts: 0),
            Sidecar("clipboard", now.AddMinutes(4), RecordingStage.ClipboardSet, attempts: 0),
            Sidecar("deferred", now.AddMinutes(5), RecordingStage.PasteDeferred, attempts: 0),
            Sidecar("failed", now.AddMinutes(6), RecordingStage.Failed, attempts: 0),
            Sidecar("recovered", now.AddMinutes(7), RecordingStage.Recovered, attempts: 0),
            Sidecar("exhausted-capture", now.AddMinutes(-2), RecordingStage.Captured, attempts: 3),
            Sidecar("exhausted-text", now.AddMinutes(-1), RecordingStage.Transcribed, text: "old", attempts: 4)
        };

        var plan = RecoveryPlanner.Plan(records);

        Assert.Equal(new[] { "capture", "text" }, plan.Items.Select(item => item.Id));
        Assert.Null(plan.Items[0].SavedText);
        Assert.Equal("saved", plan.Items[1].SavedText);
        Assert.Equal(new[] { "exhausted-capture", "exhausted-text" }, plan.ExhaustedIds);
    }

    private static RecordingSidecar Sidecar(string id, DateTimeOffset created, RecordingStage stage, string? text = null, int attempts = 0) =>
        new(id, created, stage, "harness", text, null, null, attempts, null);
}
