using Somning.Core.Trigger;
using Xunit;

namespace Somning.Core.Tests.Trigger;

public sealed class TriggerInterpreterTests
{
    private static KeyEvent Down(long t) => new(KeyEventKind.TriggerDown, t, false);
    private static KeyEvent Up(long t) => new(KeyEventKind.TriggerUp, t, false);
    private static KeyEvent Repeat(long t) => new(KeyEventKind.TriggerRepeat, t, false);

    [Fact]
    public void Tap_toggle_cycle()
    {
        var interpreter = new TriggerInterpreter();
        
        // Idle, Down@0 -> StartRecording (mode Unknown)
        var r1 = interpreter.OnKey(Down(0));
        Assert.Equal(TriggerCommand.StartRecording, r1.Command);
        Assert.Equal(TriggerMode.Unknown, r1.Mode);
        Assert.True(interpreter.IsRecording);

        // Up@120 -> None, mode Toggle
        var r2 = interpreter.OnKey(Up(120));
        Assert.Equal(TriggerCommand.None, r2.Command);
        Assert.Equal(TriggerMode.Toggle, r2.Mode);
        Assert.Equal(TriggerMode.Toggle, interpreter.Mode);
        Assert.True(interpreter.IsRecording);

        // Down@2000 -> StopRecording reason Tap
        var r3 = interpreter.OnKey(Down(2000));
        Assert.Equal(TriggerCommand.StopRecording, r3.Command);
        Assert.Equal(TriggerMode.Toggle, r3.Mode);
        Assert.Equal(StopReason.Tap, r3.Reason);
        Assert.False(interpreter.IsRecording);

        // Following Up@2080 -> None (swallowed)
        var r4 = interpreter.OnKey(Up(2080));
        Assert.Equal(TriggerCommand.None, r4.Command);
        Assert.False(interpreter.IsRecording);
    }

    [Fact]
    public void Hold_push_to_talk_cycle()
    {
        var interpreter = new TriggerInterpreter();

        // Idle, Down@0 -> StartRecording
        var r1 = interpreter.OnKey(Down(0));
        Assert.Equal(TriggerCommand.StartRecording, r1.Command);

        // Up@450 -> StopRecording reason Release, mode Hold
        var r2 = interpreter.OnKey(Up(450));
        Assert.Equal(TriggerCommand.StopRecording, r2.Command);
        Assert.Equal(TriggerMode.Hold, r2.Mode);
        Assert.Equal(StopReason.Release, r2.Reason);
        Assert.False(interpreter.IsRecording);
    }

    [Fact]
    public void Boundary_threshold_checks()
    {
        // 299 ms -> Toggle
        var i1 = new TriggerInterpreter();
        i1.OnKey(Down(0));
        var r1 = i1.OnKey(Up(299));
        Assert.Equal(TriggerCommand.None, r1.Command);
        Assert.Equal(TriggerMode.Toggle, r1.Mode);

        // 300 ms -> Hold (>= 300 is hold)
        var i2 = new TriggerInterpreter();
        i2.OnKey(Down(0));
        var r2 = i2.OnKey(Up(300));
        Assert.Equal(TriggerCommand.StopRecording, r2.Command);
        Assert.Equal(TriggerMode.Hold, r2.Mode);
        Assert.Equal(StopReason.Release, r2.Reason);
    }

    [Fact]
    public void Repeats_and_repeated_downs_produce_no_second_start()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));

        var rRepeat = interpreter.OnKey(Repeat(100));
        Assert.Equal(TriggerCommand.None, rRepeat.Command);

        var rDown = interpreter.OnKey(Down(200));
        Assert.Equal(TriggerCommand.None, rDown.Command);
    }

    [Fact]
    public void Held_modifiers_keep_push_to_talk_alive_without_repeats()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));

        // Down@0 then OnTick(5000, modifiersDown: true) with no repeats -> None
        var r = interpreter.OnTick(5000, chordModifiersDown: true);
        Assert.Equal(TriggerCommand.None, r.Command);
        Assert.True(interpreter.IsRecording);
    }

    [Fact]
    public void Missed_key_up_when_modifiers_up_exceeds_threshold()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));
        interpreter.OnKey(Repeat(500));
        interpreter.OnKey(Repeat(1000));

        // OnTick(1200, false) -> None
        var r1 = interpreter.OnTick(1200, chordModifiersDown: false);
        Assert.Equal(TriggerCommand.None, r1.Command);

        // OnTick(2300, false) -> StopRecording MissedKeyUp (modifiers up > 1s)
        var r2 = interpreter.OnTick(2300, chordModifiersDown: false);
        Assert.Equal(TriggerCommand.StopRecording, r2.Command);
        Assert.Equal(StopReason.MissedKeyUp, r2.Reason);
        Assert.False(interpreter.IsRecording);
    }

    [Fact]
    public void Missed_key_up_when_no_trigger_activity_while_modifiers_up()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));

        // Down@0, OnTick(1100, false) -> StopRecording MissedKeyUp (no trigger activity > 1s while modifiers up)
        var r = interpreter.OnTick(1100, chordModifiersDown: false);
        Assert.Equal(TriggerCommand.StopRecording, r.Command);
        Assert.Equal(StopReason.MissedKeyUp, r.Reason);
        Assert.False(interpreter.IsRecording);
    }

    [Fact]
    public void Modifiers_true_resets_modifiers_up_timer()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));
        interpreter.OnKey(Repeat(900));

        // OnTick(1500, false) -> None
        Assert.Equal(TriggerCommand.None, interpreter.OnTick(1500, false).Command);

        // Repeat@1550, OnTick(1600, true) -> None and clears modifiers-up timer
        interpreter.OnKey(Repeat(1550));
        Assert.Equal(TriggerCommand.None, interpreter.OnTick(1600, true).Command);

        // Repeat@1950, OnTick(2000, false) -> None
        interpreter.OnKey(Repeat(1950));
        Assert.Equal(TriggerCommand.None, interpreter.OnTick(2000, false).Command);

        // Repeat@2900, OnTick(2950, false) -> None (modifiers up only 950ms, activity 50ms ago)
        interpreter.OnKey(Repeat(2900));
        Assert.Equal(TriggerCommand.None, interpreter.OnTick(2950, false).Command);
        Assert.True(interpreter.IsRecording);
    }

    [Fact]
    public void Toggle_mode_never_yields_missed_key_up()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));
        interpreter.OnKey(Up(100)); // Toggle mode
        Assert.Equal(TriggerMode.Toggle, interpreter.Mode);

        // OnTick(any, false) never yields MissedKeyUp
        var r = interpreter.OnTick(10000, chordModifiersDown: false);
        Assert.Equal(TriggerCommand.None, r.Command);
        Assert.True(interpreter.IsRecording);
    }

    [Fact]
    public void Max_length_stops_any_recording()
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(Down(0));
        interpreter.OnKey(Up(100)); // Toggle mode

        // start + 300000 -> MaxLength
        var r = interpreter.OnTick(300000, chordModifiersDown: false);
        Assert.Equal(TriggerCommand.StopRecording, r.Command);
        Assert.Equal(StopReason.MaxLength, r.Reason);
        Assert.False(interpreter.IsRecording);
        Assert.Equal(TriggerMode.Unknown, interpreter.Mode);
    }

    [Fact]
    public void Up_without_down_is_none_and_custom_options_supported()
    {
        var interpreter = new TriggerInterpreter();
        var r = interpreter.OnKey(Up(100));
        Assert.Equal(TriggerCommand.None, r.Command);

        var custom = new TriggerInterpreter(new TriggerOptions(TapThresholdMs: 200));
        custom.OnKey(Down(0));
        var rCustom = custom.OnKey(Up(200)); // >= 200 is hold
        Assert.Equal(TriggerCommand.StopRecording, rCustom.Command);
        Assert.Equal(TriggerMode.Hold, rCustom.Mode);
    }
}
