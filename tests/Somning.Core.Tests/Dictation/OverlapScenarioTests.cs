using Somning.Core.Dictation;
using Xunit;
using static Somning.Core.Tests.Dictation.DictationTestKit;

namespace Somning.Core.Tests.Dictation;

public sealed class OverlapScenarioTests
{
    [Fact]
    public void A_completes_while_B_records_without_touching_B()
    {
        var (aTranscribing, _) = Run(DictationState.Initial, Ready(),
            Start(1000, "rec-a"), new CaptureStarted("rec-a", 40), Stop(5000), Stopped("rec-a", TargetA));
        Assert.Equal("rec-a", aTranscribing.InFlight);

        var (bRecording, bStartEffects) = Run(aTranscribing, Start(5200, "rec-b"), new CaptureStarted("rec-b", 35));
        Assert.Equal(new CaptureSlot("rec-b", CapturePhase.Recording, false), bRecording.Capture);
        Assert.DoesNotContain(bStartEffects, effect => effect is Transcribe);

        var (aDelivering, aTextEffects) = Step(bRecording, new TranscriptionCompleted("rec-a", TextResult("texto A"), 6000));
        var deliverA = Assert.Single(aTextEffects.OfType<Deliver>());
        Assert.Equal("rec-a", deliverA.Id);
        Assert.Equal(TargetA, deliverA.Target);
        Assert.Equal(CapturePhase.Recording, aDelivering.Capture!.Phase);
        Assert.DoesNotContain(aTextEffects, effect => effect is StopCapture);

        var (aDone, aDoneEffects) = Step(aDelivering, new DeliveryCompleted("rec-a", Delivery("pasted"), 6100));
        Assert.Equal(new CaptureSlot("rec-b", CapturePhase.Recording, false), aDone.Capture);
        Assert.DoesNotContain(aDoneEffects, effect => effect is StopCapture or Transcribe);
        Assert.Null(aDone.InFlight);

        var (bDone, bEffects) = Run(aDone,
            Stop(9000), Stopped("rec-b", TargetB),
            new TranscriptionCompleted("rec-b", TextResult("texto B"), 9500),
            new DeliveryCompleted("rec-b", Delivery("pasted"), 9600));
        var deliverB = Assert.Single(bEffects.OfType<Deliver>());
        Assert.Equal(TargetB, deliverB.Target);
        Assert.Equal("texto B", deliverB.Text);

        var allMetrics = Metrics(aDoneEffects.Concat(bEffects));
        Assert.Equal(["rec-a", "rec-b"], allMetrics.Select(m => m.Id).ToArray());
        Assert.Equal(1100, allMetrics[0].StopToPasteMs);
        Assert.Equal(600, allMetrics[1].StopToPasteMs);
        Assert.Empty(bDone.Records);
    }

    [Fact]
    public void Queued_recordings_are_processed_in_fifo_order_each_to_its_own_target()
    {
        var targets = new Dictionary<string, Somning.Core.Output.PasteTarget>
        {
            ["rec-1"] = TargetA,
            ["rec-2"] = TargetB,
            ["rec-3"] = TargetA with { ForegroundHwnd = 0x3001 }
        };
        var state = Step(DictationState.Initial, Ready()).State;
        var dispatched = new List<string>();
        var start = 1000L;
        foreach (var (id, target) in targets)
        {
            var (next, effects) = Run(state, Start(start, id), new CaptureStarted(id, 30), Stop(start + 500), Stopped(id, target));
            state = next;
            dispatched.AddRange(effects.OfType<Transcribe>().Select(effect => effect.Id));
            start += 1000;
        }
        Assert.Equal(["rec-1"], dispatched);
        Assert.Equal(["rec-2", "rec-3"], state.Pending.ToArray());

        var delivered = new List<Deliver>();
        foreach (var id in targets.Keys)
        {
            var (next, effects) = Run(state,
                new TranscriptionCompleted(id, TextResult("t-" + id), start),
                new DeliveryCompleted(id, Delivery("pasted"), start + 10));
            state = next;
            delivered.AddRange(effects.OfType<Deliver>());
        }

        Assert.Equal(targets.Keys.ToArray(), delivered.Select(d => d.Id).ToArray());
        Assert.All(delivered, d => Assert.Equal(targets[d.Id], d.Target));
        Assert.Null(state.InFlight);
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void Live_captures_finish_during_recovery_admission_and_dispatch_after_recovery_ids()
    {
        var initial = DictationState.Initial with { RecoveryAdmissionPending = true };
        var effects = new List<DictationEffect>();

        var (state, liveEffects) = Run(initial, Ready(),
            Start(1000, "live-1"), new CaptureStarted("live-1", 30), Stop(2000), Stopped("live-1", TargetA),
            Start(2500, "live-2"), new CaptureStarted("live-2", 30), Stop(3500), Stopped("live-2", TargetB));
        effects.AddRange(liveEffects);
        Assert.Null(state.Capture);
        Assert.Equal(2, liveEffects.OfType<StartCapture>().Count());
        Assert.DoesNotContain(liveEffects, effect => effect is Transcribe or CopyOnly);
        Assert.Equal(["live-1", "live-2"], state.Pending.ToArray());

        var (recoveryQueued, recoveryEffects) = Run(state,
            new RecoveryRequested("old-1", "C:/rec/old-1.wav", null, 0, "tap"),
            new RecoveryRequested("old-2", "C:/rec/old-2.wav", "salvo", 1, "hold"));
        Assert.Empty(recoveryEffects);

        var (admitted, admitEffects) = Step(recoveryQueued, new RecoveryAdmissionCompleted());
        Assert.False(admitted.RecoveryAdmissionPending);
        Assert.Equal("old-1", Assert.IsType<Transcribe>(Assert.Single(admitEffects)).Id);
        Assert.Equal(["old-2", "live-1", "live-2"], admitted.Pending.ToArray());
        effects.AddRange(admitEffects);

        var (afterOld1, old1Effects) = Run(admitted,
            new TranscriptionCompleted("old-1", TextResult("velho"), 4000),
            new DeliveryCompleted("old-1", Delivery("clipboard_set"), 4010));
        effects.AddRange(old1Effects);
        var (afterOld2, old2Effects) = Step(afterOld1, new DeliveryCompleted("old-2", Delivery("clipboard_set"), 4020));
        effects.AddRange(old2Effects);
        var (afterLive1, live1Effects) = Run(afterOld2,
            new TranscriptionCompleted("live-1", TextResult("um"), 4100),
            new DeliveryCompleted("live-1", Delivery("pasted"), 4110));
        effects.AddRange(live1Effects);
        var (done, live2Effects) = Run(afterLive1,
            new TranscriptionCompleted("live-2", TextResult("dois"), 4200),
            new DeliveryCompleted("live-2", Delivery("pasted"), 4210));
        effects.AddRange(live2Effects);

        var dispatches = effects
            .Select(effect => effect switch { Transcribe t => "T:" + t.Id, CopyOnly c => "C:" + c.Id, _ => null })
            .OfType<string>()
            .ToArray();
        Assert.Equal(["T:old-1", "C:old-1", "C:old-2", "T:live-1", "T:live-2"], dispatches);
        Assert.Equal(["live-1", "live-2"], effects.OfType<Deliver>().Select(d => d.Id).ToArray());
        Assert.Equal(["recovered", "recovered", "pasted", "pasted"], Metrics(effects).Select(m => m.Outcome).ToArray());
        Assert.Empty(done.Records);
        Assert.Empty(done.Pending);
    }

    [Fact]
    public void Admission_completion_with_no_engine_ready_dispatches_saved_text_but_waits_for_transcription()
    {
        var initial = DictationState.Initial with { RecoveryAdmissionPending = true };
        var (state, _) = Run(initial,
            new RecoveryRequested("old-1", "C:/rec/old-1.wav", "salvo", 0, "tap"),
            new RecoveryRequested("old-2", "C:/rec/old-2.wav", null, 0, "tap"));

        var (admitted, effects) = Step(state, new RecoveryAdmissionCompleted());
        Assert.Equal("old-1", Assert.IsType<CopyOnly>(Assert.Single(effects)).Id);

        var (waiting, copyDone) = Step(admitted, new DeliveryCompleted("old-1", Delivery("clipboard_set"), 10));
        Assert.DoesNotContain(copyDone, effect => effect is Transcribe);
        Assert.Equal(["old-2"], waiting.Pending.ToArray());

        var (_, readyEffects) = Step(waiting, Ready());
        Assert.Equal("old-2", Assert.IsType<Transcribe>(Assert.Single(readyEffects)).Id);
    }
}
