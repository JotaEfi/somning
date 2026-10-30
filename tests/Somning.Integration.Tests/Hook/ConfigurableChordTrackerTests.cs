using Somning.Core.Settings;
using Somning.Core.Trigger;
using Somning.Platform.Windows.Hook;
using Xunit;

namespace Somning.Integration.Tests.Hook;

/// <summary>Pure event replay. No native hook, SendInput, keyboard or window interaction.</summary>
public sealed class ConfigurableChordTrackerTests
{
    [Theory]
    [InlineData("Escape", KeyModifiers.None, 0x1B)]
    [InlineData("Enter", KeyModifiers.Control, 0x0D)]
    public void ActionsCanBePressedWhileHoldingActivationModifiers(string key, KeyModifiers modifiers, uint vk)
    {
        var tracker = new ChordTracker();
        var action = new HotkeyBinding(modifiers, key);
        tracker.Process(0x11, true, action, false, out _);
        tracker.Process(0x10, true, action, false, out _);
        Assert.False(tracker.CanStart(vk, action));
        Assert.True(tracker.CanStart(vk, action, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.True(tracker.Process(vk, true, action, true, out var kind, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(KeyEventKind.TriggerDown, kind);
        Assert.True(tracker.Process(vk, false, action, false, out kind));
        Assert.Equal(KeyEventKind.TriggerUp, kind);
    }

    [Fact]
    public void ReinstallWithBothControlKeysHeldRetainsRightControlAfterLeftRelease()
    {
        var tracker = new ChordTracker();
        var binding = new HotkeyBinding(KeyModifiers.Control, "K");
        tracker.ResyncPhysical(key => key is 0xA2 or 0xA3 or 0x11);
        tracker.Process(0xA2, false, binding, true, out _);
        Assert.True(tracker.Process(0x4B, true, binding, true, out var kind));
        Assert.Equal(KeyEventKind.TriggerDown, kind);
    }

    [Fact]
    public void AltAndWindowsWithRightSideModifiersAreRecognized()
    {
        var tracker = new ChordTracker();
        var binding = new HotkeyBinding(KeyModifiers.Alt | KeyModifiers.Windows, "F8");
        tracker.Process(0xA5, true, binding, true, out _);
        tracker.Process(0x5C, true, binding, true, out _);
        Assert.True(tracker.Process(0x77, true, binding, true, out var kind));
        Assert.Equal(KeyEventKind.TriggerDown, kind);
        Assert.True(tracker.Process(0x77, true, binding, true, out kind));
        Assert.Equal(KeyEventKind.TriggerRepeat, kind);
        tracker.Process(0xA5, false, binding, true, out _);
        tracker.Process(0x5C, false, binding, true, out _);
        Assert.True(tracker.Process(0x77, false, binding, true, out kind));
        Assert.Equal(KeyEventKind.TriggerUp, kind);
    }

    [Fact]
    public void ModifierReleaseAndRebindingStillSwallowOwnedKeyUp()
    {
        var tracker = new ChordTracker();
        var old = new HotkeyBinding(KeyModifiers.Control, "K");
        tracker.Process(0xA2, true, old, true, out _);
        Assert.True(tracker.Process(0x4B, true, old, true, out _));
        tracker.Process(0xA2, false, old, true, out _);
        Assert.True(tracker.Process(0x4B, false, new(KeyModifiers.Alt, "F8"), false, out var kind));
        Assert.Equal(KeyEventKind.TriggerUp, kind);
    }

    [Fact]
    public void DisabledActionsPassThroughUntilOwned()
    {
        var tracker = new ChordTracker();
        var binding = new HotkeyBinding(KeyModifiers.None, "Escape");
        Assert.False(tracker.Process(0x1B, true, binding, false, out _));
        Assert.False(tracker.Process(0x1B, false, binding, false, out _));
        Assert.True(tracker.Process(0x1B, true, binding, true, out _));
        Assert.True(tracker.Process(0x1B, false, binding, false, out var kind));
        Assert.Equal(KeyEventKind.TriggerUp, kind);
    }

    [Fact]
    public void ExtraModifiersDoNotTriggerAnotherConfiguredChord()
    {
        var tracker = new ChordTracker();
        var binding = new HotkeyBinding(KeyModifiers.Control, "K");
        tracker.Process(0x11, true, binding, true, out _);
        tracker.Process(0x10, true, binding, true, out _);
        Assert.False(tracker.Process(0x4B, true, binding, true, out _));
    }

    [Fact]
    public void CustomKeyResyncReportsReleaseAndRetainsModifierState()
    {
        var tracker = new ChordTracker();
        var binding = new HotkeyBinding(KeyModifiers.Alt, "F8");
        tracker.Process(0x12, true, binding, true, out _);
        tracker.Process(0x77, true, binding, true, out _);
        Assert.True(tracker.Resync(KeyModifiers.Alt, false));
        Assert.True(tracker.Process(0x77, true, binding, true, out var kind));
        Assert.Equal(KeyEventKind.TriggerDown, kind);
    }
}
