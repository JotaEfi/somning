using System.Threading.Channels;
using Somning.Core.Trigger;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Hook;
using Xunit;

namespace Somning.Integration.Tests.Hook;

[Trait("Category", "Desktop")]
public sealed class HoldWatchdogTests
{
    private static Channel<KeyEvent> CreateQueue() => Channel.CreateBounded<KeyEvent>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    [Fact]
    public async Task Held_chord_3500ms_is_not_cut()
    {
        Requires.Desktop();
        using var window = new HotkeyTestWindow();
        var queue = CreateQueue();
        using var hook = new KeyboardHook(queue.Writer);
        hook.Start();

        await using var loop = new HookTriggerLoop(queue.Reader);

        try
        {
            HotkeyInjector.Down();
            await Task.Delay(3500, TestContext.Current.CancellationToken);

            // Assert exactly one StartRecording and zero StopRecording before Up
            var duringHold = loop.Results.ToArray();
            Assert.Contains(duringHold, r => r.Result.Command == TriggerCommand.StartRecording);
            Assert.DoesNotContain(duringHold, r => r.Result.Command == TriggerCommand.StopRecording);

            HotkeyInjector.Up();
            await Task.Delay(300, TestContext.Current.CancellationToken);

            var afterUp = loop.Results.ToArray();
            var stopResult = Assert.Single(afterUp, r => r.Result.Command == TriggerCommand.StopRecording);
            Assert.Equal(TriggerMode.Hold, stopResult.Result.Mode);
            Assert.Equal(StopReason.Release, stopResult.Result.Reason);
        }
        finally
        {
            HotkeyInjector.ReleaseAll();
        }
    }

    [Fact]
    public async Task Lost_trigger_up_is_caught_by_modifier_watchdog()
    {
        Requires.Desktop();
        using var window = new HotkeyTestWindow();
        var queue = CreateQueue();
        using var hook = new KeyboardHook(queue.Writer);
        hook.Start();

        await using var loop = new HookTriggerLoop(queue.Reader);

        try
        {
            HotkeyInjector.Down();
            await Task.Delay(600, TestContext.Current.CancellationToken);

            HotkeyInjector.TriggerOnly(true); // Activity signal immediately before release
            long activityTime = Environment.TickCount64;

            HotkeyInjector.ModifiersUpOnly(); // Ctrl + Shift up, Space left held
            long releaseTime = Environment.TickCount64;

            // Wait 800 ms (less than 1000 ms threshold) - no StopRecording yet
            await Task.Delay(800, TestContext.Current.CancellationToken);
            var earlyResults = loop.Results.ToArray();
            Assert.DoesNotContain(earlyResults, r => r.Result.Command == TriggerCommand.StopRecording);

            // Wait another 700 ms (total 1500 ms > 1000 ms)
            await Task.Delay(700, TestContext.Current.CancellationToken);

            var finalResults = loop.Results.ToArray();
            var stopResult = Assert.Single(finalResults, r => r.Result.Command == TriggerCommand.StopRecording);
            Assert.Equal(StopReason.MissedKeyUp, stopResult.Result.Reason);

            // Now release the owned trigger key
            HotkeyInjector.TriggerOnly(false);
            await Task.Delay(100, TestContext.Current.CancellationToken);

            // Verify no further commands
            Assert.Single(loop.Results.ToArray(), r => r.Result.Command == TriggerCommand.StopRecording);
        }
        finally
        {
            HotkeyInjector.ReleaseAll();
        }
    }
}
