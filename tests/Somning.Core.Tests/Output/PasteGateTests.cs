using Somning.Core.Output;
using Xunit;

namespace Somning.Core.Tests.Output;

public sealed class PasteGateTests
{
    private static PasteTarget ValidTarget(nint hwnd = 100, nint focus = 200, uint pid = 1, uint tid = 1, int rid = 0x3000) =>
        new(hwnd, focus, pid, tid, FocusQueried: true, IntegrityRid: rid);

    [Fact]
    public void All_conditions_met_should_paste()
    {
        var target = ValidTarget();
        var current = ValidTarget();
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.True(decision.ShouldPaste);
        Assert.Null(decision.DeferReason);
    }

    [Fact]
    public void Clipboard_not_set_defers_clipboard_failed()
    {
        var target = ValidTarget();
        var current = ValidTarget();
        var ctx = new PasteContext(ClipboardSet: false, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("clipboard_failed", decision.DeferReason);
    }

    [Fact]
    public void No_target_window_defers_no_target()
    {
        var target = new PasteTarget(0, 0, 0, 0, false, null);
        var current = ValidTarget();
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("no_target", decision.DeferReason);
    }

    [Fact]
    public void Modifiers_held_defers_modifiers_held()
    {
        var target = ValidTarget();
        var current = ValidTarget();
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: false, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("modifiers_held", decision.DeferReason);
    }

    [Fact]
    public void Foreground_window_changed_defers_foreground_changed()
    {
        var target = ValidTarget(hwnd: 100);
        var current = ValidTarget(hwnd: 999);
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("foreground_changed", decision.DeferReason);
    }

    [Fact]
    public void Process_changed_defers_foreground_changed()
    {
        var target = ValidTarget(hwnd: 100, pid: 10);
        var current = ValidTarget(hwnd: 100, pid: 20);
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("foreground_changed", decision.DeferReason);
    }

    [Fact]
    public void Focus_unknown_when_query_failed_or_zero()
    {
        var target = new PasteTarget(100, 0, 1, 1, FocusQueried: false, IntegrityRid: 0x3000);
        var current = ValidTarget(hwnd: 100);
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("focus_unknown", decision.DeferReason);
    }

    [Fact]
    public void Focus_changed_in_same_window_defers_focus_changed()
    {
        var target = ValidTarget(hwnd: 100, focus: 201);
        var current = ValidTarget(hwnd: 100, focus: 202);
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("focus_changed", decision.DeferReason);
    }

    [Fact]
    public void Integrity_unknown_defers_integrity_unknown()
    {
        var target = new PasteTarget(100, 200, 1, 1, FocusQueried: true, IntegrityRid: null);
        var current = ValidTarget(hwnd: 100, focus: 200);
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000);

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("integrity_unknown", decision.DeferReason);
    }

    [Fact]
    public void Target_elevated_defers_target_elevated()
    {
        var target = ValidTarget(hwnd: 100, focus: 200, rid: 0x4000); // System/Admin
        var current = ValidTarget(hwnd: 100, focus: 200);
        var ctx = new PasteContext(ClipboardSet: true, ModifiersReleased: true, current, target, OwnIntegrityRid: 0x3000); // Medium

        var decision = PasteGate.Decide(ctx);

        Assert.False(decision.ShouldPaste);
        Assert.Equal("target_elevated", decision.DeferReason);
    }
}
