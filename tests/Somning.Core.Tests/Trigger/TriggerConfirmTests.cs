using Somning.Core.Trigger;
using Xunit;

namespace Somning.Core.Tests.Trigger;

public sealed class TriggerConfirmTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmStopsHoldOrToggleAndReleasedActivationCannotRestart(bool toggle)
    {
        var interpreter = new TriggerInterpreter();
        interpreter.OnKey(new(KeyEventKind.TriggerDown, 0, false));
        if (toggle) interpreter.OnKey(new(KeyEventKind.TriggerUp, 100, false));
        var result = interpreter.OnKey(new(KeyEventKind.Confirm, 500, false));
        Assert.Equal(TriggerCommand.StopRecording, result.Command);
        Assert.Equal(toggle ? TriggerMode.Toggle : TriggerMode.Hold, result.Mode);
        Assert.False(interpreter.IsRecording);
        Assert.Equal(TriggerCommand.None, interpreter.OnKey(new(KeyEventKind.TriggerUp, 600, false)).Command);
        Assert.Equal(TriggerCommand.None, interpreter.OnKey(new(KeyEventKind.Confirm, 650, false)).Command);
        Assert.Equal(TriggerCommand.StartRecording, interpreter.OnKey(new(KeyEventKind.TriggerDown, 700, false)).Command);
    }

    [Fact]
    public void ConfirmWhileIdleDoesNothing()
    {
        var interpreter = new TriggerInterpreter();
        Assert.Equal(TriggerCommand.None, interpreter.OnKey(new(KeyEventKind.Confirm, 0, false)).Command);
        Assert.False(interpreter.IsRecording);
    }
}
