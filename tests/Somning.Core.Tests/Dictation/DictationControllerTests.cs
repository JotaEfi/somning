using Somning.Core.Dictation;
using Somning.Core.Recording;
using Xunit;
using static Somning.Core.Tests.Dictation.DictationTestKit;

namespace Somning.Core.Tests.Dictation;

public sealed class DictationControllerTests
{
    [Fact]
    public void Happy_path_tap_toggle_emits_one_metrics()
    {
        var state = Run(DictationState.Initial, Ready()).State;

        var (afterStart, startEffects) = Step(state, Start(1000, "rec-1"));
        Assert.Equal(CapturePhase.Starting, afterStart.Capture!.Phase);
        Assert.Collection(startEffects,
            effect => Assert.Equal(RecordingStage.Captured, Assert.IsType<UpdateSidecar>(effect).Sidecar.Stage),
            effect => Assert.Equal("rec-1", Assert.IsType<StartCapture>(effect).Id));

        var (recording, startedEffects) = Step(afterStart, new CaptureStarted("rec-1", 40));
        Assert.Equal(CapturePhase.Recording, recording.Capture!.Phase);
        Assert.Empty(startedEffects);

        var (stopping, stopEffects) = Step(recording, Stop(6000));
        Assert.Equal(CapturePhase.Stopping, stopping.Capture!.Phase);
        Assert.Equal("rec-1", Assert.IsType<StopCapture>(Assert.Single(stopEffects)).Id);

        var (queued, stoppedEffects) = Step(stopping, Stopped("rec-1", TargetA));
        Assert.Null(queued.Capture);
        Assert.Equal("rec-1", queued.InFlight);
        var transcribe = Assert.IsType<Transcribe>(Assert.Single(stoppedEffects));
        Assert.Equal("C:/rec/rec-1.wav", transcribe.WavPath);

        const string text = "Olá, mundo.";
        var (transcribed, textEffects) = Step(queued, new TranscriptionCompleted("rec-1", TextResult(text), 7200));
        Assert.Collection(textEffects,
            effect =>
            {
                var sidecar = Assert.IsType<UpdateSidecar>(effect).Sidecar;
                Assert.Equal(RecordingStage.Transcribed, sidecar.Stage);
                Assert.Equal(text, sidecar.Text);
            },
            effect =>
            {
                var deliver = Assert.IsType<Deliver>(effect);
                Assert.Equal(text, deliver.Text);
                Assert.Equal(TargetA, deliver.Target);
            });

        var (done, deliveryEffects) = Step(transcribed, new DeliveryCompleted("rec-1", Delivery("pasted"), 7300));
        Assert.Collection(deliveryEffects,
            effect => Assert.Equal(RecordingStage.Pasted, Assert.IsType<UpdateSidecar>(effect).Sidecar.Stage),
            effect => Assert.IsType<EmitMetrics>(effect));

        var metrics = Assert.Single(Metrics(deliveryEffects));
        Assert.Equal("rec-1", metrics.Id);
        Assert.Equal("tap", metrics.Mode);
        Assert.Equal(5000, metrics.CaptureMs);
        Assert.Equal(40, metrics.MicOpenMs);
        Assert.Equal(12, metrics.VadMs);
        Assert.Equal(3, metrics.QueueMs);
        Assert.Equal(900, metrics.InferMs);
        Assert.Equal("vulkan", metrics.Backend);
        Assert.Equal(30, metrics.PasteMs);
        Assert.Equal(1300, metrics.StopToPasteMs);
        Assert.Equal("pasted", metrics.Outcome);
        Assert.Null(metrics.Reason);
        Assert.Equal(text.Length, metrics.Chars);

        Assert.Null(done.Capture);
        Assert.Null(done.InFlight);
        Assert.Empty(done.Pending);
        Assert.Empty(done.Records);
    }

    [Fact]
    public void Metrics_fields_follow_the_single_schema()
    {
        var metrics = new DictationMetrics("id", "tap", 1, 2, 3, 4, 5, 6, 7, "cpu", "m", 8, 9, "pasted", null, 10);

        var fields = metrics.ToFields();

        Assert.Equal(DictationMetrics.FieldNames, fields.Keys.ToArray());
        Assert.Contains("load_ms", DictationMetrics.FieldNames);
        Assert.Contains("warm_ms", DictationMetrics.FieldNames);
        Assert.Contains("stop_to_paste_ms", DictationMetrics.FieldNames);
        Assert.Equal(6L, fields["load_ms"]);
        Assert.Equal(7L, fields["warm_ms"]);
        Assert.DoesNotContain(DictationMetrics.FieldNames, name => name is "text" or "transcript");
    }
}
