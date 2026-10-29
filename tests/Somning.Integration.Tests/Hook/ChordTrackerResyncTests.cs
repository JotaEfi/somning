using Somning.Core.Trigger;
using Somning.Platform.Windows.Hook;
using Xunit;

namespace Somning.Integration.Tests.Hook;

/// <summary>
/// Headless (pure struct, no desktop). Regression for the leaked-Space hypothesis: a hook reinstall used to zero the
/// ChordTracker, so a chord completed right after the reinstall was not recognised and Space reached the focused app.
/// </summary>
public sealed class ChordTrackerResyncTests
{
    private const uint Ctrl = 0x11, Shift = 0x10, Space = 0x20;

    [Fact]
    public void Zeroed_tracker_misses_a_chord_whose_modifiers_are_already_down_which_leaks_space()
    {
        var tracker = default(ChordTracker); // what the old reinstall did
        Assert.False(tracker.Process(Space, down: true, out _));
    }

    [Fact]
    public void Resync_with_modifiers_down_recognises_the_next_space_as_the_trigger()
    {
        var tracker = default(ChordTracker);
        Assert.False(tracker.Resync(ctrlDown: true, shiftDown: true, spaceDown: false));

        Assert.True(tracker.Process(Space, down: true, out var kind));
        Assert.Equal(KeyEventKind.TriggerDown, kind);
    }

    [Fact]
    public void Resync_keeps_an_owned_chord_when_space_is_still_held_and_the_release_is_still_swallowed()
    {
        var tracker = default(ChordTracker);
        _ = tracker.Process(Ctrl, true, out _); _ = tracker.Process(Shift, true, out _);
        Assert.True(tracker.Process(Space, true, out _));

        Assert.False(tracker.Resync(true, true, spaceDown: true));

        Assert.True(tracker.Process(Space, down: false, out var kind));
        Assert.Equal(KeyEventKind.TriggerUp, kind);
    }

    [Fact]
    public void Resync_reports_a_missed_release_so_a_trigger_up_can_be_emitted()
    {
        var tracker = default(ChordTracker);
        _ = tracker.Process(Ctrl, true, out _); _ = tracker.Process(Shift, true, out _);
        Assert.True(tracker.Process(Space, true, out _));

        Assert.True(tracker.Resync(ctrlDown: false, shiftDown: false, spaceDown: false));

        // The chord is no longer owned: a plain Space must pass through.
        Assert.False(tracker.Process(Space, down: true, out _));
    }

    [Fact]
    public void Resync_clears_modifiers_released_during_the_gap_so_they_are_not_stuck()
    {
        var tracker = default(ChordTracker);
        _ = tracker.Process(Ctrl, true, out _); _ = tracker.Process(Shift, true, out _);

        Assert.False(tracker.Resync(ctrlDown: false, shiftDown: false, spaceDown: false));

        Assert.False(tracker.Process(Space, down: true, out _));
    }
}
