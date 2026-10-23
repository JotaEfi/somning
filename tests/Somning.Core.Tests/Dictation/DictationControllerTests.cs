using Somning.Core.Dictation;
using Somning.Core.Recording;
using Somning.Core.Trigger;
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

    [Theory]
    [InlineData(TriggerMode.Toggle, StopReason.Tap, "tap", null)]
    [InlineData(TriggerMode.Hold, StopReason.Release, "hold", null)]
    [InlineData(TriggerMode.Hold, StopReason.MaxLength, "hold", "max_length")]
    [InlineData(TriggerMode.Hold, StopReason.MissedKeyUp, "hold", "missed_key_up")]
    [InlineData(TriggerMode.Toggle, StopReason.MaxLength, "tap", "max_length")]
    public void Stop_mode_and_reason_flow_into_metrics(TriggerMode mode, StopReason reason, string expectedMode, string? expectedReason)
    {
        var state = InFlightRecording("rec-1", mode, reason);

        var (_, effects) = Run(state,
            new TranscriptionCompleted("rec-1", TextResult("oi"), 7000),
            new DeliveryCompleted("rec-1", Delivery("pasted"), 7100));

        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal(expectedMode, metrics.Mode);
        Assert.Equal(expectedReason, metrics.Reason);
        Assert.Equal("pasted", metrics.Outcome);
    }

    [Fact]
    public void Stale_or_unknown_ids_leave_state_unchanged_without_effects()
    {
        var inFlight = InFlightRecording("rec-1");
        DictationEvent[] stale =
        [
            new TranscriptionCompleted("other", TextResult("x"), 7000),
            new DeliveryCompleted("rec-1", Delivery("pasted"), 7000),
            new DeliveryCompleted("other", Delivery("pasted"), 7000),
            new CaptureStopped("other", TargetB, 100, "C:/rec/other.wav"),
            new CaptureStarted("other", 10),
            new CaptureStartFailed("other", "no_device"),
            new CaptureFaulted("other", "device_lost", "gone", 7000)
        ];

        foreach (var e in stale)
        {
            var (next, effects) = Step(inFlight, e);
            Assert.Same(inFlight, next);
            Assert.Empty(effects);
        }

        var recording = Run(DictationState.Initial, Start(1000, "rec-2"), new CaptureStarted("rec-2", 40)).State;
        var (afterStale, staleEffects) = Step(recording, new CaptureStopped("rec-1", TargetA, 100, "C:/rec/rec-1.wav"));
        Assert.Same(recording, afterStale);
        Assert.Empty(staleEffects);
    }

    [Fact]
    public void Stop_while_starting_is_remembered_and_executed_on_capture_started()
    {
        var starting = Step(DictationState.Initial, Start(1000, "rec-1")).State;

        var (remembered, stopEffects) = Step(starting, Stop(1100));
        Assert.Empty(stopEffects);
        Assert.True(remembered.Capture!.StopRequested);
        Assert.Equal(CapturePhase.Starting, remembered.Capture.Phase);

        var (stopping, startedEffects) = Step(remembered, new CaptureStarted("rec-1", 60));
        Assert.Equal("rec-1", Assert.IsType<StopCapture>(Assert.Single(startedEffects)).Id);
        Assert.Equal(CapturePhase.Stopping, stopping.Capture!.Phase);
        Assert.Equal(1100, stopping.Records["rec-1"].StopMs);
    }

    [Fact]
    public void Start_while_stopping_is_executed_after_the_previous_capture_stops()
    {
        var stopping = Run(DictationState.Initial, Ready(), Start(1000, "rec-a"), new CaptureStarted("rec-a", 40), Stop(5000)).State;

        var (waiting, startEffects) = Step(stopping, Start(5010, "rec-b"));
        Assert.Empty(startEffects);
        Assert.Equal("rec-b", waiting.NextStart!.Id);
        Assert.Equal("rec-a", waiting.Capture!.Id);

        var (started, effects) = Step(waiting, Stopped("rec-a", TargetA));
        Assert.Null(started.NextStart);
        Assert.Equal(new CaptureSlot("rec-b", CapturePhase.Starting, false), started.Capture);
        Assert.Collection(effects,
            effect =>
            {
                var sidecar = Assert.IsType<UpdateSidecar>(effect).Sidecar;
                Assert.Equal("rec-b", sidecar.Id);
                Assert.Equal(RecordingStage.Captured, sidecar.Stage);
            },
            effect => Assert.Equal("rec-b", Assert.IsType<StartCapture>(effect).Id),
            effect => Assert.Equal("rec-a", Assert.IsType<Transcribe>(effect).Id));
        Assert.Equal("rec-a", started.InFlight);
    }

    [Fact]
    public void Empty_transcription_marks_sidecar_empty_without_delivery()
    {
        var (state, effects) = Step(InFlightRecording(), new TranscriptionCompleted("rec-1", Empty(), 7000));

        Assert.DoesNotContain(effects, effect => effect is Deliver or CopyOnly);
        Assert.Equal(RecordingStage.Empty, Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar.Stage);
        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal("empty", metrics.Outcome);
        Assert.Equal(0, metrics.Chars);
        Assert.Null(state.InFlight);
        Assert.Empty(state.Records);
    }

    [Fact]
    public void Whitespace_only_text_is_treated_as_empty()
    {
        var (_, effects) = Step(InFlightRecording(), new TranscriptionCompleted("rec-1", TextResult("   "), 7000));

        Assert.DoesNotContain(effects, effect => effect is Deliver or CopyOnly);
        Assert.Equal("empty", Assert.Single(Metrics(effects)).Outcome);
    }

    [Fact]
    public void Retryable_error_keeps_recording_captured_and_recoverable()
    {
        var (state, effects) = Step(InFlightRecording(), new TranscriptionCompleted("rec-1", Error("engine_failed", true), 7000));

        var sidecar = Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar;
        Assert.Equal(RecordingStage.Captured, sidecar.Stage);
        Assert.Equal(1, sidecar.Attempts);
        Assert.Equal("engine_failed", sidecar.LastError);
        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal("error", metrics.Outcome);
        Assert.Equal("engine_failed", metrics.Reason);
        Assert.DoesNotContain(effects, effect => effect is Deliver or CopyOnly);
        Assert.Null(state.InFlight);
    }

    [Fact]
    public void Non_retryable_error_marks_recording_failed()
    {
        var (_, effects) = Step(InFlightRecording(), new TranscriptionCompleted("rec-1", Error("invalid_path", false), 7000));

        var sidecar = Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar;
        Assert.Equal(RecordingStage.Failed, sidecar.Stage);
        Assert.Equal("invalid_path", sidecar.LastError);
        Assert.Equal("error", Assert.Single(Metrics(effects)).Outcome);
    }

    [Fact]
    public void Clipboard_failure_keeps_transcribed_text_and_counts_an_attempt()
    {
        var (state, effects) = Step(TranscribedRecording("rec-1", "texto salvo"),
            new DeliveryCompleted("rec-1", Delivery("clipboard_failed", "open_clipboard_failed"), 7100));

        var sidecar = Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar;
        Assert.Equal(RecordingStage.Transcribed, sidecar.Stage);
        Assert.Equal("texto salvo", sidecar.Text);
        Assert.Equal(1, sidecar.Attempts);
        Assert.Equal("clipboard_failed", sidecar.LastError);
        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal("error", metrics.Outcome);
        Assert.Equal("clipboard_failed", metrics.Reason);
        Assert.Null(state.InFlight);
    }

    [Fact]
    public void Paste_deferred_is_terminal_with_text_on_clipboard()
    {
        var (_, effects) = Step(TranscribedRecording("rec-1", "abc"),
            new DeliveryCompleted("rec-1", Delivery("paste_deferred", "foreground_changed"), 7100));

        Assert.Equal(RecordingStage.PasteDeferred, Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar.Stage);
        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal("paste_deferred", metrics.Outcome);
        Assert.Equal("foreground_changed", metrics.Reason);
        Assert.Equal(3, metrics.Chars);
    }

    [Fact]
    public void Capture_start_failure_clears_slot_marks_failed_and_emits_error()
    {
        var starting = Step(DictationState.Initial, Start(1000, "rec-1")).State;

        var (state, effects) = Step(starting, new CaptureStartFailed("rec-1", "no_device"));

        Assert.Null(state.Capture);
        Assert.Empty(state.Records);
        Assert.Equal(RecordingStage.Failed, Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar.Stage);
        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal("error", metrics.Outcome);
        Assert.Equal("capture_start_failed:no_device", metrics.Reason);
    }

    [Fact]
    public void Capture_fault_while_recording_keeps_audio_captured_and_emits_error()
    {
        var recording = Run(DictationState.Initial, Start(1000, "rec-1"), new CaptureStarted("rec-1", 40)).State;

        var (state, effects) = Step(recording, new CaptureFaulted("rec-1", "device_lost", "Device removed", 3000));

        Assert.Null(state.Capture);
        Assert.Empty(state.Pending);
        var sidecar = Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar;
        Assert.Equal(RecordingStage.Captured, sidecar.Stage);
        Assert.Equal("device_lost", sidecar.LastError);
        var metrics = Assert.Single(Metrics(effects));
        Assert.Equal("error", metrics.Outcome);
        Assert.Equal("capture_failed:device_lost", metrics.Reason);
        Assert.DoesNotContain(effects, effect => effect is Transcribe);
    }

    [Fact]
    public void Recording_proceeds_before_engine_ready_and_is_dispatched_once_ready()
    {
        var (state, effects) = Run(DictationState.Initial,
            Start(1000, "rec-1"), new CaptureStarted("rec-1", 40), Stop(4000), Stopped("rec-1", TargetA));

        Assert.False(state.EngineReady);
        Assert.Contains(effects, effect => effect is StartCapture);
        Assert.Contains(effects, effect => effect is StopCapture);
        Assert.DoesNotContain(effects, effect => effect is Transcribe);
        Assert.Equal(["rec-1"], state.Pending.ToArray());

        var (ready, readyEffects) = Step(state, Ready());
        Assert.Equal("rec-1", Assert.IsType<Transcribe>(Assert.Single(readyEffects)).Id);
        Assert.Equal("rec-1", ready.InFlight);
    }

    [Fact]
    public void Recovery_with_saved_text_is_copied_only_and_marked_recovered()
    {
        var (state, effects) = Run(DictationState.Initial,
            new RecoveryRequested("old-1", "C:/rec/old-1.wav", "texto antigo", 1, "hold"));

        Assert.Equal("texto antigo", Assert.IsType<CopyOnly>(Assert.Single(effects)).Text);
        Assert.DoesNotContain(effects, effect => effect is Transcribe or Deliver);

        var (done, doneEffects) = Step(state, new DeliveryCompleted("old-1", Delivery("clipboard_set"), 900));
        Assert.Equal(RecordingStage.Recovered, Assert.Single(doneEffects.OfType<UpdateSidecar>()).Sidecar.Stage);
        var metrics = Assert.Single(Metrics(doneEffects));
        Assert.Equal("recovered", metrics.Outcome);
        Assert.Equal("hold", metrics.Mode);
        Assert.Null(done.InFlight);
        Assert.Empty(done.Records);
    }

    [Fact]
    public void Recovery_without_saved_text_is_transcribed_then_copied_never_pasted()
    {
        var (state, effects) = Run(DictationState.Initial, Ready(),
            new RecoveryRequested("old-1", "C:/rec/old-1.wav", null, 0, "tap"));
        Assert.Equal("C:/rec/old-1.wav", Assert.IsType<Transcribe>(Assert.Single(effects)).WavPath);

        var (copied, copyEffects) = Step(state, new TranscriptionCompleted("old-1", TextResult("recuperado"), 500));
        Assert.Collection(copyEffects,
            effect => Assert.Equal(RecordingStage.Transcribed, Assert.IsType<UpdateSidecar>(effect).Sidecar.Stage),
            effect => Assert.Equal("recuperado", Assert.IsType<CopyOnly>(effect).Text));

        var (_, doneEffects) = Step(copied, new DeliveryCompleted("old-1", Delivery("clipboard_set"), 600));
        Assert.Equal("recovered", Assert.Single(Metrics(doneEffects)).Outcome);
    }

    [Fact]
    public void Recovery_clipboard_failure_keeps_text_recoverable()
    {
        var state = Step(DictationState.Initial, new RecoveryRequested("old-1", "C:/rec/old-1.wav", "abc", 1, "tap")).State;

        var (_, effects) = Step(state, new DeliveryCompleted("old-1", Delivery("clipboard_failed"), 600));

        var sidecar = Assert.Single(effects.OfType<UpdateSidecar>()).Sidecar;
        Assert.Equal(RecordingStage.Transcribed, sidecar.Stage);
        Assert.Equal(2, sidecar.Attempts);
        Assert.Equal("error", Assert.Single(Metrics(effects)).Outcome);
    }

    [Fact]
    public void Duplicate_recovery_request_for_a_known_id_is_ignored()
    {
        var state = Step(DictationState.Initial, new RecoveryRequested("old-1", "C:/rec/old-1.wav", "abc", 0, "tap")).State;

        var (next, effects) = Step(state, new RecoveryRequested("old-1", "C:/rec/old-1.wav", "abc", 0, "tap"));

        Assert.Same(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Every_completed_dictation_or_recovery_emits_exactly_one_metrics()
    {
        DictationEvent[][] endings =
        [
            [new TranscriptionCompleted("rec-1", TextResult("a"), 7000), new DeliveryCompleted("rec-1", Delivery("pasted"), 7100)],
            [new TranscriptionCompleted("rec-1", TextResult("a"), 7000), new DeliveryCompleted("rec-1", Delivery("paste_deferred"), 7100)],
            [new TranscriptionCompleted("rec-1", TextResult("a"), 7000), new DeliveryCompleted("rec-1", Delivery("clipboard_failed"), 7100)],
            [new TranscriptionCompleted("rec-1", Empty(), 7000)],
            [new TranscriptionCompleted("rec-1", Error("engine_failed", true), 7000)],
            [new TranscriptionCompleted("rec-1", Error("bad_request", false), 7000)]
        ];

        foreach (var ending in endings)
        {
            var (state, effects) = Run(InFlightRecording(), ending);
            Assert.Single(Metrics(effects));
            Assert.Empty(state.Records);
            Assert.Null(state.InFlight);
        }
    }
}
