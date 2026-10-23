using System.Threading.Channels;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Hook;
using Somning.Platform.Windows.Clipboard;
using Somning.Integration.Tests.Support;
using Xunit;
namespace Somning.Integration.Tests.Hook;
[Trait("Category", "Desktop")]
public sealed class KeyboardHookTests
{
    private static Channel<KeyEvent> Queue() => Channel.CreateBounded<KeyEvent>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private static async Task<KeyEvent> Read(Channel<KeyEvent> queue) =>
        await queue.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    [Fact]
    public async Task Chord_is_suppressed_and_enqueued()
    {
        Requires.Desktop(); using var window = new HotkeyTestWindow(); using var observer = new ObserverHook();
        var queue = Queue(); using var hook = new KeyboardHook(queue.Writer); hook.Start();
        try
        {
            HotkeyInjector.Chord(50);
            Assert.Equal(KeyEventKind.TriggerDown, (await Read(queue)).Kind);
            Assert.Equal(KeyEventKind.TriggerUp, (await Read(queue)).Kind);
            Assert.Contains(observer.Events, e => e.Key is 0x11 or 0xA2 or 0xA3);
            Assert.Contains(observer.Events, e => e.Key is 0x10 or 0xA0 or 0xA1);
            Assert.DoesNotContain(observer.Events, e => e.Key == 0x20);
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }
    [Fact]
    public async Task Paste_marker_passes_through()
    {
        Requires.Desktop(); using var window = new HotkeyTestWindow(); using var observer = new ObserverHook();
        using var notepad = NotepadDriver.Start();
        Requires.That(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit), "could not acquire test-owned Notepad foreground");
        using var clipboard = new ClipboardWriter();
        var textSet = clipboard.SetText("Somning paste hook test.");
        Requires.That(textSet.Success, "could not set test clipboard contents");
        var queue = Queue(); using var hook = new KeyboardHook(queue.Writer); hook.Start();
        try
        {
            HotkeyInjector.PasteChord(); await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.False(queue.Reader.TryRead(out _));
            Assert.Contains(observer.Events, e => e.Key == 0x56 && e.Down);
            Assert.Contains(observer.Events, e => e.Key == 0x56 && !e.Down);
            Assert.Equal("Somning paste hook test.", notepad.WaitForText("Somning paste hook test."));
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }
    [Fact]
    public async Task Space_without_modifiers_is_not_suppressed()
    {
        Requires.Desktop(); using var window = new HotkeyTestWindow(); using var observer = new ObserverHook();
        var queue = Queue(); using var hook = new KeyboardHook(queue.Writer); hook.Start();
        try
        {
            HotkeyInjector.TriggerOnly(true); HotkeyInjector.TriggerOnly(false);
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Contains(observer.Events, e => e.Key == 0x20 && e.Down);
            Assert.False(queue.Reader.TryRead(out _));
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }
    [Fact]
    public async Task Auto_repeat_downs_are_repeat_events()
    {
        Requires.Desktop(); using var window = new HotkeyTestWindow(); using var observer = new ObserverHook();
        var queue = Queue(); using var hook = new KeyboardHook(queue.Writer); hook.Start();
        try
        {
            HotkeyInjector.Down();
            for (int i = 0; i < 3; i++) HotkeyInjector.TriggerOnly(true);
            HotkeyInjector.Up();
            Assert.Equal(KeyEventKind.TriggerDown, (await Read(queue)).Kind);
            for (int i = 0; i < 3; i++) Assert.Equal(KeyEventKind.TriggerRepeat, (await Read(queue)).Kind);
            Assert.Equal(KeyEventKind.TriggerUp, (await Read(queue)).Kind);
            Assert.DoesNotContain(observer.Events, e => e.Key == 0x20);
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }
    [Fact]
    public async Task Modifiers_async_state_tracks_injected_chord()
    {
        Requires.Desktop(); using var window = new HotkeyTestWindow();
        var queue = Queue(); using var hook = new KeyboardHook(queue.Writer); hook.Start();
        try
        {
            HotkeyInjector.Down(); await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.True(PhysicalKeyState.AreChordModifiersDown());
            HotkeyInjector.Up(); await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(PhysicalKeyState.AreChordModifiersDown());
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }
}
