using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Paste;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Paste;

[Trait("Category", "Desktop")]
public sealed class TargetPasterTests
{
    [Fact]
    public async Task Pastes_into_notepad_and_keeps_clipboard()
    {
        Requires.Desktop();
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "could not acquire foreground");
        using var clipboard = new ClipboardWriter();
        var integrity = new IntegrityReader();
        var paster = new TargetPaster(clipboard, new ForegroundProbe(integrity), integrity,
            new ModifierWaiter(), new PasteInjector());
        var target = new ForegroundProbe(integrity).Capture();

        var result = await paster.DeliverAsync("Olá, ação às 10h.", target, CancellationToken.None);

        Assert.Equal("pasted", result.Outcome);
        Assert.Equal("Olá, ação às 10h.", notepad.WaitForText("Olá, ação às 10h."));
        Assert.Equal("Olá, ação às 10h.", ClipboardProbe.ReadText());
    }

    [Fact]
    public async Task Foreground_changed_defers_and_leaves_text_on_clipboard()
    {
        Requires.Desktop();
        using var first = NotepadDriver.Start();
        using var second = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(first.Window, first.Edit), "could not acquire first Notepad foreground");
        using var clipboard = new ClipboardWriter();
        var integrity = new IntegrityReader();
        var probe = new ForegroundProbe(integrity);
        var target = probe.Capture();
        Requires.That(ForegroundHelper.EnsureForeground(second.Window, second.Edit), "could not acquire second Notepad foreground");
        var paster = new TargetPaster(clipboard, probe, integrity, new ModifierWaiter(), new PasteInjector());

        var result = await paster.DeliverAsync("Não colar.", target, CancellationToken.None);

        Assert.Equal("paste_deferred", result.Outcome);
        Assert.Equal("foreground_changed", result.Reason);
        Assert.Equal("", first.GetText());
        Assert.Equal("", second.GetText());
        Assert.Equal("Não colar.", ClipboardProbe.ReadText());
    }

    [Fact]
    public async Task Copy_only_never_pastes()
    {
        Requires.Desktop();
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "could not acquire foreground");
        using var clipboard = new ClipboardWriter();
        var integrity = new IntegrityReader();
        var paster = new TargetPaster(clipboard, new ForegroundProbe(integrity), integrity,
            new ModifierWaiter(), new PasteInjector());

        var result = await paster.CopyOnlyAsync("Recuperado.", CancellationToken.None);

        Assert.Equal("clipboard_set", result.Outcome);
        Assert.Equal("", notepad.GetText());
        Assert.Equal("Recuperado.", ClipboardProbe.ReadText());
    }

    [Fact]
    public async Task Clipboard_change_during_modifier_wait_defers_before_injection()
    {
        var clipboard = new ControlledClipboardWriter();
        var target = new PasteTarget(10, 20, 30, 40, true, 0x3000);
        var probe = new FixedProbe(target);
        var injector = new RecordingPasteInjector();
        var paster = new TargetPaster(clipboard, probe, new FixedIntegrityReader(),
            new CopyingModifierWaiter(clipboard), injector);

        var result = await paster.DeliverAsync("Somning.", target, CancellationToken.None);

        Assert.Equal("paste_deferred", result.Outcome);
        Assert.Equal("clipboard_changed", result.Reason);
        Assert.Equal(1, probe.CaptureCount);
        Assert.Equal(0, injector.SendCount);
    }

    [Fact]
    public async Task Focus_change_inside_same_top_level_window_defers()
    {
        Requires.Desktop();
        using var window = TwoEditWindow.Start();
        window.FocusEdit(0);
        Requires.That(ForegroundHelper.EnsureForeground(window.Window, GetEditForFocus(window, 0)),
            "could not acquire two-edit test foreground");
        window.FocusEdit(0);
        var integrity = new IntegrityReader();
        var probe = new ForegroundProbe(integrity);
        var target = probe.Capture();
        window.FocusEdit(1);
        using var clipboard = new ClipboardWriter();
        var paster = new TargetPaster(clipboard, probe, integrity, new ModifierWaiter(), new PasteInjector());

        var result = await paster.DeliverAsync("Olá.", target, CancellationToken.None);

        Assert.Equal("paste_deferred", result.Outcome);
        Assert.Equal("focus_changed", result.Reason);
        Assert.Equal("", window.GetText(0));
        Assert.Equal("", window.GetText(1));
        Assert.Equal("Olá.", ClipboardProbe.ReadText());
    }

    private sealed class ControlledClipboardWriter : IClipboardWriter
    {
        private uint sequence = 100;
        public ClipboardSetResult SetText(string text)
        {
            sequence++;
            return new(true, sequence - 1, sequence, 1, null);
        }
        public uint GetSequenceNumber() => sequence;
        public void SimulateExternalCopy() => sequence++;
    }

    private sealed class CopyingModifierWaiter(ControlledClipboardWriter clipboard) : IModifierWaiter
    {
        public Task<bool> WaitForReleaseAsync(TimeSpan? timeout = null, TimeSpan? poll = null, CancellationToken ct = default)
        {
            clipboard.SimulateExternalCopy();
            return Task.FromResult(true);
        }
    }

    private sealed class FixedProbe(PasteTarget target) : ITargetProbe
    {
        public int CaptureCount { get; private set; }
        public PasteTarget Capture() { CaptureCount++; return target; }
    }

    private sealed class FixedIntegrityReader : IIntegrityReader
    {
        public int? ReadRid(uint processId) => 0x3000;
        public int OwnRid() => 0x3000;
    }

    private sealed class RecordingPasteInjector : IPasteInjector
    {
        public int SendCount { get; private set; }
        public PasteInjectionResult SendCtrlV() { SendCount++; return new(4, true); }
    }

    private static nint GetEditForFocus(TwoEditWindow window, int index) => window.EditHandle(index);
}
