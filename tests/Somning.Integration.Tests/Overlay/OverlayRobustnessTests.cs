using System.Collections.Concurrent;
using System.ComponentModel;
using Somning.App.Dictation;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Overlay;
using Somning.Core.Ports;
using Somning.Core.Recording;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Overlay;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

/// <summary>
/// The overlay must never stay on "Transcribing" when the engine is dead or silent, and real microphone levels
/// must reach it with enough gain to be visible. Headless: fake audio, fake engine, fake overlay.
/// </summary>
public sealed class OverlayRobustnessTests : IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);
    private readonly string recordingsDir = Path.Combine(Path.GetTempPath(), "somning-overlay-robust", Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<DictationEvent> events = new();
    private readonly RecordingOverlay overlay = new();
    private SidecarStore store = null!;
    private EffectRunner? runner;

    public ValueTask InitializeAsync()
    {
        store = new SidecarStore(recordingsDir);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (runner is not null) await runner.DisposeAsync();
        if (Directory.Exists(recordingsDir)) Directory.Delete(recordingsDir, recursive: true);
    }

    private EffectRunner Runner(ITranscriber transcriber, IAudioSource? source = null, TimeSpan? ceiling = null) =>
        runner = new EffectRunner(events.Enqueue, store, recordingsDir, () => source ?? new ToneSource(0f), new NoProbe(),
            transcriber, new NoOutput(), getOverlay: () => overlay, audioTracker: new AudioLevelTracker(), transcribeCeiling: ceiling);

    private async Task<T> WaitFor<T>(Func<T, bool> match) where T : DictationEvent
    {
        var until = DateTime.UtcNow + EventTimeout;
        while (DateTime.UtcNow < until)
        {
            var hit = events.OfType<T>().FirstOrDefault(match);
            if (hit is not null) return hit;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"No {typeof(T).Name} within {EventTimeout}.");
    }

    private static DictationMetrics ErrorMetrics(string reason) =>
        new("id", "tap", 1000, 5, 0, 0, 0, 0, 0, "cpu", "m", 0, 0, "error", reason, 0);

    [Fact]
    public async Task A_transcriber_that_never_answers_times_out_with_a_recoverable_error()
    {
        var effects = Runner(new HangingTranscriber(), ceiling: TimeSpan.FromMilliseconds(300));

        effects.Run([new Transcribe("hang", Path.Combine(recordingsDir, "hang.wav"))]);
        var completed = await WaitFor<TranscriptionCompleted>(e => e.Id == "hang");

        Assert.Equal(OverlayNotices.TranscribeTimeoutCode, completed.Result.ErrorCode);
        Assert.Equal("error", completed.Result.Outcome);
        Assert.True(completed.Result.Retryable, "the recording must stay recoverable");
    }

    [Fact]
    public async Task An_engine_that_fails_to_start_reports_an_error_instead_of_hanging()
    {
        var effects = Runner(new ThrowingTranscriber(new Win32Exception(2)));

        effects.Run([new Transcribe("dead", Path.Combine(recordingsDir, "dead.wav"))]);
        var completed = await WaitFor<TranscriptionCompleted>(e => e.Id == "dead");

        Assert.Equal("engine_exception", completed.Result.ErrorCode);
        Assert.True(completed.Result.Retryable);
    }

    [Theory]
    [InlineData("engine_failed", OverlayNoticeKind.EngineFailed)]
    [InlineData("engine_retry_failed", OverlayNoticeKind.EngineFailed)]
    [InlineData("engine_exception", OverlayNoticeKind.EngineFailed)]
    [InlineData("transcribe_timeout", OverlayNoticeKind.TranscribeTimeout)]
    [InlineData("no_network", OverlayNoticeKind.CloudNoNetwork)]
    public void Failed_transcription_shows_an_error_notice_instead_of_staying_on_transcribing(string reason, OverlayNoticeKind expected)
    {
        var effects = Runner(new ThrowingTranscriber(new InvalidOperationException()));
        overlay.ShowTranscribing();

        effects.Run([new EmitMetrics(ErrorMetrics(reason))]);

        Assert.Equal(OverlayMode.Notice, overlay.CurrentModel.Mode);
        Assert.Equal(expected, overlay.CurrentModel.Notice);
        Assert.DoesNotContain("Transcrevendo", overlay.CurrentModel.Text);
    }

    [Fact]
    public void Clipboard_and_capture_failures_do_not_get_the_transcription_notice()
    {
        var effects = Runner(new ThrowingTranscriber(new InvalidOperationException()));
        overlay.ShowTranscribing();

        effects.Run([new EmitMetrics(ErrorMetrics("clipboard_failed"))]);

        Assert.Equal(OverlayMode.Hidden, overlay.CurrentModel.Mode);
    }

    [Fact]
    public async Task Normal_microphone_levels_reach_the_overlay_with_visible_gain()
    {
        // RMS ~0.05 (-26 dBFS): normal speech on a typical microphone.
        var effects = Runner(new ThrowingTranscriber(new InvalidOperationException()), new ToneSource(0.05f));

        effects.Run([new StartCapture("tone", "tap")]);
        await WaitFor<CaptureStarted>(e => e.Id == "tone");
        await Task.Delay(500, TestContext.Current.CancellationToken);
        effects.Run([new StopCapture("tone")]);
        await WaitFor<CaptureStopped>(e => e.Id == "tone");

        // The animator applies the perceptual gain to the raw level the sink delivers.
        var drive = new VoiceLevelNormalizer().Update(overlay.PeakLevel, 0.03f);
        Assert.True(drive > 0.5f, $"normal speech only reached visual level {drive}");
    }

    private sealed class RecordingOverlay : IOverlayView
    {
        private float peakLevel;

        public IntPtr Hwnd => 1;
        public bool IsVisible => CurrentModel.Mode != OverlayMode.Hidden;
        public OverlayViewModel CurrentModel { get; private set; } = OverlayViewModel.CreateHidden();
        public float PeakLevel => Volatile.Read(ref peakLevel);

        public void ShowListening(float[] waveformBars) => CurrentModel = OverlayViewModel.CreateListening(waveformBars);

        public void UpdateWaveform(float[] waveformBars)
        {
            var level = AudioLevelTracker.LevelFromBars(waveformBars);
            if (level > PeakLevel) Volatile.Write(ref peakLevel, level);
        }

        public void ShowTranscribing() => CurrentModel = OverlayViewModel.CreateTranscribing();
        public void ShowNotice(OverlayNoticeKind kind) => CurrentModel = OverlayViewModel.CreateNotice(kind);
        public void Hide() => CurrentModel = OverlayViewModel.CreateHidden();
        public void Dispose() { }
    }

    private sealed class NoProbe : ITargetProbe
    {
        public PasteTarget Capture() => new(1, 2, 3, 4, true, 8192);
    }

    private sealed class NoOutput : ITextOutput
    {
        public Task<DeliveryResult> DeliverAsync(string text, PasteTarget target, CancellationToken ct) =>
            Task.FromResult(new DeliveryResult("pasted", null, 1, 2, 3));

        public Task<DeliveryResult> CopyOnlyAsync(string text, CancellationToken ct) =>
            Task.FromResult(new DeliveryResult("clipboard_set", null, 1, 0, 3));
    }

    private abstract class StubTranscriber : ITranscriber
    {
        public EngineStatus Status => EngineStatus.Ready;
        public string? ActiveBackend => "cpu";
        public event Action<EngineStatus>? StatusChanged { add { } remove { } }
        public Task StartAsync(string backend, CancellationToken ct) => Task.CompletedTask;
        public abstract Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class HangingTranscriber : StubTranscriber
    {
        public override async Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class ThrowingTranscriber(Exception error) : StubTranscriber
    {
        public override Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct) => Task.FromException<EngineTranscription>(error);
    }

    /// <summary>Emits a 220 Hz tone with the requested RMS (0 = silence) in 20 ms frames.</summary>
    private sealed class ToneSource(float rms) : IAudioSource
    {
        private readonly CancellationTokenSource stop = new();
        private Task pump = Task.CompletedTask;

        public event Action<float>? Level { add { } remove { } }
        public event Action<AudioFault>? Faulted { add { } remove { } }

        public Task<AudioStartInfo> StartAsync(IPcmSink sink, CancellationToken ct)
        {
            pump = Task.Run(async () =>
            {
                var frame = new byte[640];
                var amplitude = rms * MathF.Sqrt(2f) * short.MaxValue;
                long n = 0;
                while (!stop.IsCancellationRequested)
                {
                    for (var i = 0; i < frame.Length; i += 2, n++)
                    {
                        var sample = (short)(amplitude * MathF.Sin(2f * MathF.PI * 220f * n / 16000f));
                        frame[i] = (byte)(sample & 0xFF);
                        frame[i + 1] = (byte)(sample >> 8);
                    }

                    sink.Write(frame);
                    try { await Task.Delay(20, stop.Token); } catch (OperationCanceledException) { break; }
                }
            }, CancellationToken.None);
            return Task.FromResult(new AudioStartInfo(1, "tone"));
        }

        public async Task StopAsync()
        {
            stop.Cancel();
            await pump;
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            await pump;
        }
    }
}
