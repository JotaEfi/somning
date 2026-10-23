using System.Collections.Concurrent;
using System.Collections.Immutable;
using NAudio.Wave;
using Somning.App.Dictation;
using Somning.Core.Dictation;
using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Recording;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Dictation;

/// <summary>EffectRunner adapter contracts (reviews H6, H8, M9, M11). No desktop, no engine, no models.</summary>
public sealed class EffectRunnerTests : IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);
    private static readonly PasteTarget Target = new(1, 2, 3, 4, true, 8192);
    private readonly string recordingsDir = Path.Combine(Path.GetTempPath(), "somning-effects", Guid.NewGuid().ToString("N"));
    private readonly EventSink sink = new();
    private readonly FakeTextOutput output = new();
    private readonly FakeTranscriber transcriber = new();
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

    private EffectRunner Runner(FakeAudioSource source) =>
        runner = new EffectRunner(sink.Post, store, recordingsDir, () => source, new FixedProbe(), transcriber, output);

    private static RecordingSidecar Sidecar(string id, RecordingStage stage, string? text = null) =>
        new(id, DateTimeOffset.UnixEpoch, stage, "tap", text, null, null, 0, null);

    private string WavPath(string id) => Path.Combine(recordingsDir, id + ".wav");

    [Fact]
    public async Task Stop_while_starting_waits_for_start_then_stops_once()
    {
        var source = new FakeAudioSource { StartDelay = TimeSpan.FromMilliseconds(500) };
        var effects = Runner(source);

        effects.Run([new StartCapture("a", "tap")]);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        effects.Run([new StopCapture("a")]);

        var stopped = await sink.WaitForAsync<CaptureStopped>(e => e.Id == "a", EventTimeout);
        await effects.WhenIdleAsync(EventTimeout);

        Assert.False(source.StopCalledBeforeStartCompleted, "StopAsync ran before StartAsync completed.");
        Assert.True(source.StartCompleted);
        Assert.Single(sink.Of<CaptureStarted>());
        Assert.Single(sink.Of<CaptureStopped>());
        Assert.Equal(Target, stopped.Target);
        Assert.Equal(Path.GetFullPath(WavPath("a")), Path.GetFullPath(stopped.WavPath));
        using var exclusive = new FileStream(WavPath("a"), FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Fact]
    public async Task Start_failure_posts_CaptureStartFailed_and_releases_the_spool()
    {
        var source = new FakeAudioSource
        {
            StartDelay = TimeSpan.FromMilliseconds(100),
            StartException = new AudioDeviceUnavailableException("no_device", "No microphone.")
        };
        var effects = Runner(source);

        effects.Run([new StartCapture("b", "tap")]);
        var failed = await sink.WaitForAsync<CaptureStartFailed>(e => e.Id == "b", EventTimeout);
        await effects.WhenIdleAsync(EventTimeout);

        Assert.Equal("no_device", failed.Reason);
        Assert.Empty(sink.Of<CaptureStopped>());
        Assert.Empty(sink.Of<CaptureStarted>());
        using var exclusive = new FileStream(WavPath("b"), FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Fact]
    public async Task Mid_capture_fault_finalizes_spool_and_posts_one_CaptureFaulted()
    {
        var source = new FakeAudioSource { FaultAfter = TimeSpan.FromMilliseconds(300), FaultCode = "device_lost" };
        var effects = Runner(source);

        effects.Run([new UpdateSidecar(Sidecar("c", RecordingStage.Captured)), new StartCapture("c", "tap")]);
        var faulted = await sink.WaitForAsync<CaptureFaulted>(e => e.Id == "c", EventTimeout);
        await effects.WhenIdleAsync(EventTimeout);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal("device_lost", faulted.Code);
        Assert.InRange(Environment.TickCount64 - faulted.NowMs, 0, 1000 + 200 + 5000);
        Assert.Single(sink.Of<CaptureFaulted>());
        Assert.Empty(sink.Of<CaptureStopped>());
        Assert.True(source.StopCalls >= 1, "The faulted source was not stopped.");
        using (var exclusive = new FileStream(WavPath("c"), FileMode.Open, FileAccess.Read, FileShare.None))
        using (var reader = new WaveFileReader(exclusive))
        {
            Assert.Equal(16_000, reader.WaveFormat.SampleRate);
            Assert.True(reader.Length > 0, "Spool has no audio.");
        }
        Assert.Equal(RecordingStage.Captured, store.Load("c")!.Stage);
    }

    [Fact]
    public async Task UpdateSidecar_is_persisted_before_the_next_effect_of_the_batch_runs()
    {
        var effects = Runner(new FakeAudioSource());
        output.OnDeliver = () => store.Load("d");

        effects.Run([new UpdateSidecar(Sidecar("d", RecordingStage.Transcribed, "Olá mundo")), new Deliver("d", "Olá mundo", Target)]);
        await sink.WaitForAsync<DeliveryCompleted>(e => e.Id == "d", EventTimeout);

        var seen = output.Seen;
        Assert.NotNull(seen);
        Assert.Equal(RecordingStage.Transcribed, seen!.Stage);
        Assert.Equal("Olá mundo", seen.Text);
    }

    [Fact]
    public async Task Completion_events_are_stamped_with_TickCount64()
    {
        var effects = Runner(new FakeAudioSource());

        effects.Run([new Transcribe("e", WavPath("e")), new CopyOnly("f", "texto")]);
        var transcribed = await sink.WaitForAsync<TranscriptionCompleted>(e => e.Id == "e", EventTimeout);
        var delivered = await sink.WaitForAsync<DeliveryCompleted>(e => e.Id == "f", EventTimeout);
        var now = Environment.TickCount64;

        Assert.InRange(now - transcribed.CompletedMs, 0, 1000);
        Assert.InRange(now - delivered.CompletedMs, 0, 1000);
        Assert.Equal("text", transcribed.Result.Outcome);
        Assert.Equal("clipboard_set", delivered.Result.Outcome);
    }

    [Fact]
    public void Sidecar_CreatedUtc_is_stamped_once_and_preserved()
    {
        var effects = Runner(new FakeAudioSource());
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        effects.Run([new UpdateSidecar(Sidecar("g", RecordingStage.Captured))]);
        var first = store.Load("g")!.CreatedUtc;
        Thread.Sleep(20);
        effects.Run([new UpdateSidecar(Sidecar("g", RecordingStage.Transcribed, "oi"))]);
        var second = store.Load("g")!;

        Assert.InRange(first, before, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(first, second.CreatedUtc);
        Assert.Equal(RecordingStage.Transcribed, second.Stage);
    }

    // ---- Fakes ------------------------------------------------------------------------------------------------

    private sealed class EventSink
    {
        private readonly ConcurrentQueue<DictationEvent> events = new();

        public void Post(DictationEvent e) => events.Enqueue(e);

        public IReadOnlyList<T> Of<T>() where T : DictationEvent => events.OfType<T>().ToArray();

        public async Task<T> WaitForAsync<T>(Func<T, bool> match, TimeSpan timeout) where T : DictationEvent
        {
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                if (events.OfType<T>().FirstOrDefault(match) is { } found) return found;
                await Task.Delay(10);
            }
            Assert.Fail($"{typeof(T).Name} not posted within {timeout}. Posted: {string.Join(", ", events.Select(e => e.GetType().Name))}");
            return null!;
        }
    }

    private sealed class FixedProbe : ITargetProbe
    {
        public PasteTarget Capture() => Target;
    }

    private sealed class FakeTextOutput : ITextOutput
    {
        public Func<RecordingSidecar?>? OnDeliver { get; set; }
        public RecordingSidecar? Seen { get; private set; }

        public Task<DeliveryResult> DeliverAsync(string text, PasteTarget target, CancellationToken ct)
        {
            Seen = OnDeliver?.Invoke();
            return Task.FromResult(new DeliveryResult("pasted", null, 1, 2, 3));
        }

        public Task<DeliveryResult> CopyOnlyAsync(string text, CancellationToken ct) =>
            Task.FromResult(new DeliveryResult("clipboard_set", null, 1, 0, 3));
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public EngineStatus Status => EngineStatus.Ready;
        public string? ActiveBackend => "cpu";
        public event Action<EngineStatus>? StatusChanged { add { } remove { } }
        public Task StartAsync(string backend, CancellationToken ct) => Task.CompletedTask;

        public async Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
        {
            await Task.Delay(50, ct);
            return new EngineTranscription("Olá.", "text", null, null, false, 1000, 1, 2, 3, 4, 5, "cpu", "fake", 0);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
