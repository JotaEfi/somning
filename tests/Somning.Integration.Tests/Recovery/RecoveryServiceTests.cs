using System.Buffers.Binary;
using System.Globalization;
using NAudio.Wave;
using Somning.Core.Recording;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Recording;
using Xunit;

namespace Somning.Integration.Tests.Recovery;

/// <summary>
/// Startup recovery in the running App (plan 01-11, AUD-04, D-11, reviews H6/H9/M7): recovery is serialized through the
/// dictation FIFO, places text on the clipboard without pasting, survives a held clipboard, applies retention, and never
/// touches live captures made while admission is pending.
/// </summary>
[Trait("Category", "E2E")]
public sealed class RecoveryServiceTests
{
    private const string SavedText = "Texto salvo.";
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(180);
    private static string Tracer => Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav");
    private static string TracerText => File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt"));

    [Fact]
    public async Task Startup_recovers_pending_and_applies_retention()
    {
        Requires.Models();
        Requires.Desktop();
        using var seed = new SeededDataDir();
        var now = DateTimeOffset.UtcNow;
        var a = seed.Captured(now.AddHours(-1), Tracer);
        var b = seed.OrphanWithZeroedDataLength(now.AddHours(-2), Tracer);
        var c = seed.Transcribed(now.AddHours(-3), SavedText);
        for (var index = 0; index < 22; index++) seed.Pasted(now.AddDays(-2).AddMinutes(-index));

        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "Notepad could not be brought to the foreground.");
        using var app = await AppHarnessDriver.StartAsync(Tracer, waitForEngine: false, dataDir: seed.DataDir);

        await app.WaitEventAsync("recovery_complete", 0, null, RecoveryTimeout); // replayed if it already happened (H10)
        var outcomes = await app.EventsAsync("outcome");

        Assert.Equal([c, b, a], outcomes.Select(o => o.Id).ToArray()); // oldest first
        Assert.All(outcomes, o => Assert.Equal("recovered", o.Outcome));
        Assert.Null(await app.TryWaitEventAsync("transcribe_started", 0, c, TimeSpan.FromMilliseconds(100))); // saved text reused
        var clipboard = ClipboardProbe.ReadText();
        Assert.True(Wer.Compute(TracerText, clipboard) <= 0.10, $"Clipboard should hold a's text (processed last): '{clipboard}'");
        foreach (var id in new[] { a, b, c })
            Assert.Equal(RecordingStage.Recovered, seed.Store.Load(id)!.Stage);
        Assert.Equal("", notepad.GetText()); // recovery never pastes
        var log = app.ReadLog();
        Assert.Contains("recovery adopted orphan recording " + b, log, StringComparison.Ordinal);
        Assert.Contains("recovery repaired wav header " + b, log, StringComparison.Ordinal);
        var jsons = Directory.GetFiles(seed.RecordingsDir, "*.json").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        var wavs = Directory.GetFiles(seed.RecordingsDir, "*.wav").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Assert.Equal(RecoveryCount, jsons.Count);
        Assert.True(jsons.SetEquals(wavs), "Every remaining recording is a wav + json pair.");
        Assert.Contains(a, jsons);
        Assert.Contains(b, jsons);
        Assert.Contains(c, jsons);
    }

    [Fact]
    public async Task Clipboard_held_during_recovery_retries_next_start()
    {
        Requires.Models();
        Requires.Desktop();
        using var seed = new SeededDataDir();
        var c = seed.Transcribed(DateTimeOffset.UtcNow.AddHours(-3), SavedText);

        ClipboardHolder? holder = new ClipboardHolder();
        try
        {
            using var first = await AppHarnessDriver.StartAsync(Tracer, waitForEngine: false, dataDir: seed.DataDir);
            var failed = await first.WaitOutcomeAsync(0, TimeSpan.FromSeconds(60));
            Assert.Equal(c, failed.Id);
            Assert.Equal("error", failed.Outcome);
            Assert.Equal("clipboard_failed", failed.Reason);
            var kept = seed.Store.Load(c)!;
            Assert.Equal(RecordingStage.Transcribed, kept.Stage);
            Assert.Equal(1, kept.Attempts);
            Assert.Equal(SavedText, kept.Text);
            holder.Dispose();
            holder = null;
            await first.ShutdownAsync();
        }
        finally { holder?.Dispose(); }

        using var second = await AppHarnessDriver.StartAsync(Tracer, waitForEngine: false, dataDir: seed.DataDir);
        var recovered = await second.WaitOutcomeAsync(0, TimeSpan.FromSeconds(60));
        Assert.Equal(c, recovered.Id);
        Assert.Equal("recovered", recovered.Outcome);
        Assert.Equal(SavedText, ClipboardProbe.ReadText());
        Assert.Equal(RecordingStage.Recovered, seed.Store.Load(c)!.Stage);
    }

    [Fact]
    public async Task Early_captures_during_delayed_recovery_are_admitted_once()
    {
        Requires.Models();
        Requires.Desktop();
        using var seed = new SeededDataDir();
        var now = DateTimeOffset.UtcNow;
        var a = seed.Captured(now.AddHours(-1), Tracer);
        var c = seed.Transcribed(now.AddHours(-3), SavedText);
        var env = new Dictionary<string, string> { ["SOMNING_TEST_HOLD_RECOVERY"] = "1" };

        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "Notepad could not be brought to the foreground.");
        using var app = await AppHarnessDriver.StartAsync(Tracer, env, waitForEngine: false, dataDir: seed.DataDir);

        // Two complete live captures while recovery admission is held (no engine-readiness wait).
        await TapTapAsync();
        var live1 = await app.WaitEventAsync("target_captured", 0, null, TimeSpan.FromSeconds(10));
        await TapTapAsync();
        var live2 = await app.WaitEventAsync("target_captured", live1.Seq, null, TimeSpan.FromSeconds(10));
        var liveIds = new[] { live1.Id!, live2.Id! };
        Assert.NotEqual(live1.Id, live2.Id);
        Assert.DoesNotContain(a, liveIds);
        Assert.DoesNotContain(c, liveIds);
        foreach (var id in liveIds) await WaitForDurableAudioAsync(Path.Combine(seed.RecordingsDir, id + ".wav"));
        var held = await app.StatusAsync();
        Assert.True(held.Hook);
        Assert.False(held.RecoveryComplete);
        Assert.Null(await app.TryWaitEventAsync("outcome", 0, null, TimeSpan.FromSeconds(1))); // dispatch held, capture not
        Assert.Equal("", notepad.GetText());

        await app.ReleaseRecoveryGateAsync();
        await app.WaitEventAsync("recovery_complete", 0, null, RecoveryTimeout);
        var outcomes = new List<HarnessEvent>();
        for (long cursor = 0; outcomes.Count < 4;)
        {
            var next = await app.WaitOutcomeAsync(cursor, TimeSpan.FromSeconds(120));
            outcomes.Add(next);
            cursor = next.Seq;
        }

        Assert.Equal([c, a, live1.Id, live2.Id], outcomes.Select(o => o.Id).ToArray()); // snapshot recovery first
        Assert.Equal(["recovered", "recovered", "pasted", "pasted"], outcomes.Select(o => o.Outcome).ToArray());
        Assert.Null(await app.TryWaitEventAsync("outcome", outcomes[^1].Seq, null, TimeSpan.FromSeconds(2))); // one each
        var transcribes = await app.EventsAsync("transcribe_started");
        Assert.DoesNotContain(transcribes, t => t.Id == c);
        foreach (var id in new[] { a, live1.Id!, live2.Id! }) Assert.Single(transcribes, t => t.Id == id);
        var log = app.ReadLog();
        Assert.Equal(4, log.Split('\n').Count(line => line.Contains("[INF] dictation ", StringComparison.Ordinal)));
        foreach (var id in liveIds)
        {
            Assert.DoesNotContain("recovery requested " + id, log, StringComparison.Ordinal);
            Assert.DoesNotContain("repaired wav header " + id, log, StringComparison.Ordinal);
            Assert.DoesNotContain("adopted orphan recording " + id, log, StringComparison.Ordinal);
        }

        var second = ClipboardProbe.ReadText();
        var pasted = notepad.GetText();
        var evidence = $"\nNotepad: '{pasted}'\nClipboard: '{second}'\nOutcomes: " +
            string.Join(" | ", outcomes.Select(o => o.Metrics?.ToJsonString())) + "\nLog:\n" + log;
        Assert.True(Wer.Compute(TracerText, second) <= 0.10, "Second live text is wrong." + evidence);
        Assert.True(pasted.EndsWith(second, StringComparison.Ordinal), "Notepad should end with the second text." + evidence);
        var first = pasted[..^second.Length];
        Assert.True(Wer.Compute(TracerText, first) <= 0.10, $"First live text '{first}' is wrong." + evidence);
    }

    private const int RecoveryCount = 20;

    private static async Task TapTapAsync()
    {
        TimeSpan length;
        using (var reader = new WaveFileReader(Tracer)) length = reader.TotalTime;
        HotkeyInjector.Chord(80);
        await Task.Delay(length + TimeSpan.FromSeconds(0.8), TestContext.Current.CancellationToken);
        HotkeyInjector.Chord(80);
    }

    /// <summary>At least half a second of 16 kHz mono PCM must be on disk for the live capture.</summary>
    private static async Task WaitForDurableAudioAsync(string wavPath)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(wavPath) && new FileInfo(wavPath).Length > 44 + 16_000) return;
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.Fail($"Live capture {Path.GetFileName(wavPath)} has no durable audio.");
    }

    /// <summary>A caller-owned SOMNING_DATA_DIR seeded with recordings; deleted on dispose.</summary>
    private sealed class SeededDataDir : IDisposable
    {
        public SeededDataDir()
        {
            DataDir = Path.Combine(Path.GetTempPath(), "somning-recovery", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RecordingsDir);
            Store = new SidecarStore(RecordingsDir);
        }

        public string DataDir { get; }
        public string RecordingsDir => Path.Combine(DataDir, "recordings");
        public SidecarStore Store { get; }

        public string Captured(DateTimeOffset created, string sourceWav)
        {
            var id = IdFor(created);
            File.Copy(sourceWav, WavPath(id));
            Store.Save(new RecordingSidecar(id, created, RecordingStage.Captured, "tap", null, null, null, 0, null));
            return id;
        }

        /// <summary>A crash before finalization: data-chunk length zeroed, and no sidecar at all.</summary>
        public string OrphanWithZeroedDataLength(DateTimeOffset created, string sourceWav)
        {
            var id = IdFor(created);
            var bytes = File.ReadAllBytes(sourceWav);
            var data = bytes.AsSpan().IndexOf("data"u8);
            Assert.True(data > 0, "Fixture has no data chunk.");
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(data + 4, 4), 0);
            File.WriteAllBytes(WavPath(id), bytes);
            return id;
        }

        public string Transcribed(DateTimeOffset created, string text)
        {
            var id = IdFor(created);
            WriteTinyWav(WavPath(id));
            Store.Save(new RecordingSidecar(id, created, RecordingStage.Transcribed, "tap", text, null, null, 0, null));
            return id;
        }

        public string Pasted(DateTimeOffset created)
        {
            var id = IdFor(created);
            WriteTinyWav(WavPath(id));
            Store.Save(new RecordingSidecar(id, created, RecordingStage.Pasted, "tap", null, "pasted", null, 0, null));
            return id;
        }

        private string WavPath(string id) => Path.Combine(RecordingsDir, id + ".wav");

        private static string IdFor(DateTimeOffset created) =>
            created.ToLocalTime().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);

        private static void WriteTinyWav(string path)
        {
            using var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1));
            writer.Write(new byte[3_200], 0, 3_200);
        }

        public void Dispose()
        {
            for (var attempt = 0; attempt < 10 && Directory.Exists(DataDir); attempt++)
            {
                try { Directory.Delete(DataDir, recursive: true); }
                catch (IOException) { Thread.Sleep(200); }
                catch (UnauthorizedAccessException) { Thread.Sleep(200); }
            }
        }
    }
}
