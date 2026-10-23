using NAudio.Wave;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Harness;

/// <summary>
/// Harness pipe contract (plan 01-11, D-20, review H10): seq-numbered journal replay, command validation, status,
/// and the harness-only delivery barrier.
/// </summary>
[Trait("Category", "E2E")]
public sealed class HarnessPipeTests
{
    private static string Silence => Path.Combine(RepoPaths.Fixtures, "nonspeech", "silence.wav");
    private static string Tracer => Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav");
    private static string TracerText => File.ReadAllText(Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.txt"));

    [Fact]
    public async Task Outcome_completed_before_wait_is_replayed()
    {
        Requires.Models();
        Requires.Desktop();
        using var app = await AppHarnessDriver.StartAsync(Silence, waitForEngine: true);
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForegroundFocus(notepad.Window, notepad.Edit),
            "Notepad edit control did not become the active foreground focus.");
        var seq0 = await app.LastSeqAsync();

        HotkeyInjector.Chord(80);
        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);
        HotkeyInjector.Chord(80);

        // The outcome is complete BEFORE the client asks for it.
        await app.WaitForLogAsync("[INF] dictation ", TimeSpan.FromSeconds(30));
        var started = DateTime.UtcNow;
        var outcome = await app.WaitOutcomeAsync(seq0, TimeSpan.FromSeconds(10));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "Replay should return at once.");
        Assert.True(outcome.Seq > seq0);
        Assert.Equal("empty", outcome.Outcome);
        Assert.Equal("", notepad.GetText());
    }

    [Fact]
    public async Task Unknown_command_is_rejected()
    {
        Requires.Models();
        Requires.Desktop();
        using var app = await AppHarnessDriver.StartAsync(Silence, waitForEngine: false);

        Assert.Equal("{\"error\":\"unknown_command\"}", await app.SendRawAsync("{\"cmd\":\"nope\"}"));
        Assert.Equal("{\"error\":\"bad_request\"}", await app.SendRawAsync("not json"));
        Assert.Equal("{\"error\":\"invalid_path\"}", await app.SendRawAsync("{\"cmd\":\"set-fixture\",\"path\":\"C:\\\\nope\\\\x.txt\"}"));
    }

    [Fact]
    public async Task Status_reports_hook_and_engine()
    {
        Requires.Models();
        Requires.Desktop();
        using var app = await AppHarnessDriver.StartAsync(Silence, waitForEngine: true);

        var status = await app.StatusAsync();

        Assert.True(status.Hook);
        Assert.Equal("Ready", status.Engine);
        Assert.Equal("Idle", status.Phase);
        var engineReady = await app.WaitEventAsync("engine_ready", 0, null, TimeSpan.FromSeconds(1));
        var hook = await app.WaitEventAsync("hook_installed", 0, null, TimeSpan.FromSeconds(1));
        Assert.True(hook.Seq < engineReady.Seq, "hook_installed must precede engine_ready.");
        Assert.True(status.LastSeq >= engineReady.Seq);
    }

    [Fact]
    public async Task Delivery_barrier_holds_paste_until_released()
    {
        Requires.Models();
        Requires.Desktop();
        using var app = await AppHarnessDriver.StartAsync(Tracer, waitForEngine: true);
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForegroundFocus(notepad.Window, notepad.Edit),
            "Notepad edit control did not become the active foreground focus.");
        await app.ArmDeliveryBarrierAsync();
        var seq0 = await app.LastSeqAsync();

        await TapTapAsync(Tracer, notepad);
        var target = await app.WaitEventAsync("target_captured", seq0, null, TimeSpan.FromSeconds(10));
        await app.WaitEventAsync("transcribe_started", seq0, target.Id, TimeSpan.FromSeconds(30));

        // Held: no outcome, nothing pasted, and the pipe and owner loop stay responsive.
        Assert.Null(await app.TryWaitEventAsync("outcome", seq0, target.Id, TimeSpan.FromSeconds(8)));
        Assert.Equal("Busy", (await app.StatusAsync()).Phase);
        Assert.Equal("", notepad.GetText());

        Requires.That(ForegroundHelper.EnsureForegroundFocus(notepad.Window, notepad.Edit),
            "Notepad edit control did not retain focus before delivery-barrier release.");
        await app.ReleaseDeliveryBarrierAsync();
        var outcome = await app.WaitEventAsync("outcome", seq0, target.Id, TimeSpan.FromSeconds(30));
        Assert.Equal("pasted", outcome.Outcome);
        Assert.True(Wer.Compute(TracerText, notepad.WaitForText(ClipboardProbe.ReadText())) <= 0.10);
    }

    [Fact]
    public async Task Delivery_barrier_is_released_when_the_client_disconnects()
    {
        Requires.Models();
        Requires.Desktop();
        using var app = await AppHarnessDriver.StartAsync(Tracer, waitForEngine: true);
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "Notepad could not be brought to the foreground.");
        await app.ArmDeliveryBarrierAsync();

        await app.ConnectAsync(); // drops the arming client's connection, then reconnects
        var seq0 = await app.LastSeqAsync();
        await TapTapAsync(Tracer, notepad);

        var outcome = await app.WaitOutcomeAsync(seq0, TimeSpan.FromSeconds(60));
        Assert.Equal("pasted", outcome.Outcome);
    }

    private static async Task TapTapAsync(string fixture, NotepadDriver? notepad = null)
    {
        TimeSpan length;
        using (var reader = new WaveFileReader(fixture)) length = reader.TotalTime;
        HotkeyInjector.Chord(80);
        await Task.Delay(length + TimeSpan.FromSeconds(0.8), TestContext.Current.CancellationToken);
        if (notepad is not null)
            Requires.That(ForegroundHelper.EnsureForegroundFocus(notepad.Window, notepad.Edit),
                "Notepad edit control did not retain focus before the stop hotkey.");
        HotkeyInjector.Chord(80);
    }
}
