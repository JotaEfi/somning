using System.Threading.Channels;
using Somning.App.Composition;
using Somning.App.Dictation;
using Somning.Core.Dictation;
using Somning.Core.Ports;
using Somning.Core.Recording;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Dictation;

public sealed class CancelDictationTests : IDisposable
{
    private readonly string testDir = Path.Combine(Path.GetTempPath(), "somning-cancel-test-" + Guid.NewGuid().ToString("N"));

    public CancelDictationTests() => Directory.CreateDirectory(testDir);

    public void Dispose()
    {
        try { Directory.Delete(testDir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task CancelRequested_WhileRecording_DiscardsAudioAndDoesNotTranscribe()
    {
        // Paced audio source that runs continuously until stopped
        var fixturePath = Path.Combine(testDir, "test.wav");
        CreateDummyWav(fixturePath, 16000 * 5); // 5 seconds of audio

        var cancellationToken = TestContext.Current.CancellationToken;
        await using var app = new AppComposition(() => new FileAudioSource(fixturePath, realTime: true));

        var notifiedList = new List<string>();
        app.Host.Notified += n => notifiedList.Add(n.Kind);

        app.StartLoop();

        // 1. Trigger recording start
        var triggerDown = new KeyEvent(KeyEventKind.TriggerDown, Environment.TickCount64, true);
        app.Host.Post(new KeyReceived(triggerDown));

        // Wait until capture starts
        var startWait = DateTime.UtcNow.AddSeconds(5);
        while (app.Host.State.Capture is null && DateTime.UtcNow < startWait)
            await Task.Delay(20, cancellationToken);

        Assert.NotNull(app.Host.State.Capture);
        var captureId = app.Host.State.Capture.Id;

        // 2. User presses Escape to cancel
        var escKey = new KeyEvent(KeyEventKind.Cancel, Environment.TickCount64, true);
        app.Host.Post(new KeyReceived(escKey));

        // Wait until capture slot is released
        var cancelWait = DateTime.UtcNow.AddSeconds(5);
        while (app.Host.State.Capture is not null && DateTime.UtcNow < cancelWait)
            await Task.Delay(20, cancellationToken);

        Assert.Null(app.Host.State.Capture);

        // 3. Verify sidecar was marked cancelled
        var sidecar = app.Sidecars.Load(captureId);
        Assert.NotNull(sidecar);
        Assert.Equal(RecordingStage.Cancelled, sidecar.Stage);
        Assert.Equal("cancelled", sidecar.Outcome);
        Assert.Equal("user_cancel_esc", sidecar.Reason);

        // 4. Verify no transcription or delivery ever took place
        Assert.DoesNotContain("transcribe_started", notifiedList);
        Assert.DoesNotContain("target_captured", notifiedList);

        // 5. Verify pending queue is empty
        Assert.Empty(app.Host.State.Pending);
        Assert.Null(app.Host.State.InFlight);
    }

    private static void CreateDummyWav(string path, int samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // 1 channel
        writer.Write(16000);    // sample rate
        writer.Write(32000);    // byte rate
        writer.Write((short)2); // block align
        writer.Write((short)16); // bits per sample
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (int i = 0; i < samples; i++) writer.Write((short)0);
    }
}
