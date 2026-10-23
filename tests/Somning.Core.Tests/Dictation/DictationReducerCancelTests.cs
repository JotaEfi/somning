using Somning.Core.Dictation;
using Somning.Core.Recording;
using Xunit;
using static Somning.Core.Tests.Dictation.DictationTestKit;

namespace Somning.Core.Tests.Dictation;

public sealed class DictationReducerCancelTests
{
    [Fact]
    public void CancelRequested_WhileRecording_EmitsDiscardCaptureAndTerminalSidecar()
    {
        var recording = Run(DictationState.Initial, Ready(), Start(1000, "rec-1"), new CaptureStarted("rec-1", 40)).State;

        var (state, effects) = Step(recording, new CancelRequested(2000));

        Assert.Equal(CapturePhase.Stopping, state.Capture!.Phase);
        Assert.True(state.Capture.Cancelled);
        Assert.Equal("rec-1", state.Capture.Id);

        var record = state.Records["rec-1"];
        Assert.Equal(RecordingStage.Cancelled, record.Stage);
        Assert.Equal(2000, record.StopMs);
        Assert.Equal("user_cancel_esc", record.StopReason);

        Assert.Collection(effects,
            effect =>
            {
                var sidecar = Assert.IsType<UpdateSidecar>(effect).Sidecar;
                Assert.Equal("rec-1", sidecar.Id);
                Assert.Equal(RecordingStage.Cancelled, sidecar.Stage);
                Assert.Equal("cancelled", sidecar.Outcome);
                Assert.Equal("user_cancel_esc", sidecar.Reason);
            },
            effect => Assert.Equal("rec-1", Assert.IsType<DiscardCapture>(effect).Id));

        Assert.DoesNotContain(effects, effect => effect is StopCapture or Transcribe or Deliver or CopyOnly);
    }

    [Fact]
    public void CancelRequested_WhenIdle_IsNoOp()
    {
        var (state, effects) = Step(DictationState.Initial, new CancelRequested(500));

        Assert.Same(DictationState.Initial, state);
        Assert.Empty(effects);
    }

    [Fact]
    public void CaptureStopped_AfterCancel_DoesNotEnqueueTranscription()
    {
        var cancelled = Run(DictationState.Initial, Ready(), Start(1000, "rec-1"),
            new CaptureStarted("rec-1", 40), new CancelRequested(2000)).State;

        var (state, effects) = Step(cancelled, Stopped("rec-1", TargetA, captureMs: 1000));

        Assert.Null(state.Capture);
        Assert.Null(state.InFlight);
        Assert.Empty(state.Pending);
        Assert.Empty(state.Records);
        Assert.Empty(effects);
        Assert.DoesNotContain(effects, effect => effect is Transcribe or Deliver or CopyOnly);
    }

    [Fact]
    public void CancelRequested_WhileStarting_DiscardsBeforeTheMicrophoneOpens()
    {
        var starting = Step(DictationState.Initial, Start(1000, "rec-1")).State;

        var (cancelled, effects) = Step(starting, new CancelRequested(1050));

        Assert.Equal(CapturePhase.Stopping, cancelled.Capture!.Phase);
        Assert.True(cancelled.Capture.Cancelled);
        Assert.Equal(1050, cancelled.Records["rec-1"].StopMs);
        Assert.Equal(
            new[] { typeof(UpdateSidecar), typeof(DiscardCapture) },
            effects.Select(effect => effect.GetType()).ToArray());

        var (lateStarted, lateEffects) = Step(cancelled, new CaptureStarted("rec-1", 40));
        Assert.Equal(CapturePhase.Stopping, lateStarted.Capture!.Phase);
        Assert.Empty(lateEffects);
        Assert.Equal(RecordingStage.Cancelled, lateStarted.Records["rec-1"].Stage);
    }

    [Fact]
    public void CaptureStopped_AfterCancel_ReleasesSlotAndStartsTheDeferredCapture()
    {
        var stopping = Run(DictationState.Initial, Start(1000, "rec-a"),
            new CaptureStarted("rec-a", 40), new CancelRequested(2000)).State;
        var waiting = Step(stopping, Start(2010, "rec-b")).State;
        Assert.Equal("rec-b", waiting.NextStart!.Id);

        var (state, effects) = Step(waiting, Stopped("rec-a", TargetA));

        Assert.Null(state.NextStart);
        Assert.Equal(new CaptureSlot("rec-b", CapturePhase.Starting, false), state.Capture);
        Assert.DoesNotContain("rec-a", state.Records.Keys);
        Assert.Equal(RecordingStage.Captured, state.Records["rec-b"].Stage);
        Assert.Collection(effects,
            effect =>
            {
                var sidecar = Assert.IsType<UpdateSidecar>(effect).Sidecar;
                Assert.Equal("rec-b", sidecar.Id);
                Assert.Equal(RecordingStage.Captured, sidecar.Stage);
            },
            effect => Assert.Equal("rec-b", Assert.IsType<StartCapture>(effect).Id));
        Assert.DoesNotContain(effects, effect => effect is Transcribe or Deliver or CopyOnly);
    }

    [Fact]
    public void CaptureFaulted_AfterCancel_DoesNotResurrectTheCancelledRecording()
    {
        var cancelled = Run(DictationState.Initial, Start(1000, "rec-1"),
            new CaptureStarted("rec-1", 40), new CancelRequested(2000)).State;

        var (state, effects) = Step(cancelled, new CaptureFaulted("rec-1", "device_lost", "Device removed", 2100));

        Assert.Null(state.Capture);
        Assert.Empty(state.Records);
        Assert.Empty(effects);
    }
}
