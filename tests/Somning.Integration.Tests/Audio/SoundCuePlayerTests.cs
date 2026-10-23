using System.Collections.Concurrent;
using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class SoundCuePlayerTests
{
    [Fact]
    public async Task Play_start_beeps_without_blocking_the_caller()
    {
        var completion = new TaskCompletionSource<(int Frequency, int Duration)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new SoundCuePlayer((frequency, duration) => completion.TrySetResult((frequency, duration)));

        player.PlayStart();

        var cue = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal((SoundCuePlayer.StartFrequency, SoundCuePlayer.ShortCueMs), cue);
    }

    [Fact]
    public async Task Each_cue_uses_its_own_pattern()
    {
        var cues = new ConcurrentDictionary<int, int>();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new SoundCuePlayer((frequency, duration) =>
        {
            cues[frequency] = duration;
            if (cues.Count == 3) completion.TrySetResult(true);
        });

        player.PlayStart();
        player.PlayStop();
        player.PlayCancel();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(SoundCuePlayer.ShortCueMs, cues[SoundCuePlayer.StartFrequency]);
        Assert.Equal(SoundCuePlayer.ShortCueMs, cues[SoundCuePlayer.StopFrequency]);
        Assert.Equal(SoundCuePlayer.CancelCueMs, cues[SoundCuePlayer.CancelFrequency]);
    }

    [Fact]
    public async Task Disabled_player_stays_silent()
    {
        var calls = 0;
        var player = new SoundCuePlayer((_, _) => Interlocked.Increment(ref calls)) { Enabled = false };

        player.PlayStart();
        player.PlayStop();
        player.PlayCancel();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref calls));
    }
}
