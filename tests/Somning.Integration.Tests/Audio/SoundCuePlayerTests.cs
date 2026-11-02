using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class SoundCuePlayerTests
{
    [Fact]
    public async Task Play_start_sends_an_embedded_wav_without_blocking_the_caller()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new SoundCuePlayer(stream => completion.TrySetResult(ReadRiffTag(stream))) { Theme = SoundThemes.Drop };

        player.PlayStart();

        var tag = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("RIFF", tag);
    }

    [Fact]
    public async Task Each_cue_plays_its_own_resource_of_the_selected_theme()
    {
        var played = 0;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new SoundCuePlayer(_ =>
        {
            if (Interlocked.Increment(ref played) == 4) completion.TrySetResult(true);
        })
        { Theme = SoundThemes.Glass };

        player.PlayStart();
        player.PlayStop();
        player.PlayDone();
        player.PlayCancel();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(4, Volatile.Read(ref played));
    }

    [Fact]
    public async Task Disabled_player_stays_silent()
    {
        var calls = 0;
        var player = new SoundCuePlayer(_ => Interlocked.Increment(ref calls)) { Enabled = false };

        player.PlayStart();
        player.PlayStop();
        player.PlayDone();
        player.PlayCancel();
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.Equal(0, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Classic_theme_uses_the_original_beep_cues()
    {
        var cues = new ConcurrentBag<(int Frequency, int Duration)>();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new SoundCuePlayer(_ => { }, (frequency, duration) =>
        {
            cues.Add((frequency, duration));
            if (cues.Count == 2) completion.TrySetResult(true);
        });

        Assert.Equal(SoundThemes.Classic, player.Theme);
        player.PlayStart();
        player.PlayCancel();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains((SoundCuePlayer.StartFrequency, SoundCuePlayer.ShortCueMs), cues);
        Assert.Contains((SoundCuePlayer.CancelFrequency, SoundCuePlayer.CancelCueMs), cues);
    }

    [Fact]
    public void Unknown_theme_falls_back_to_the_default_theme()
    {
        var player = new SoundCuePlayer(_ => { });

        player.Theme = "inexistente";

        Assert.Equal(SoundCuePlayer.DefaultTheme, player.Theme);
    }

    [Fact]
    public void Every_theme_ships_a_wav_for_every_cue()
    {
        var assembly = typeof(SoundCuePlayer).Assembly;
        foreach (var (id, _) in SoundThemes.All.Where(theme => theme.Id != SoundThemes.Classic))
        {
            foreach (var cue in Enum.GetValues<SoundCue>())
            {
                using var stream = assembly.GetManifestResourceStream(SoundThemes.ResourceName(id, cue));
                Assert.NotNull(stream);
                Assert.Equal("RIFF", ReadRiffTag(stream));
            }
        }
    }

    private static string ReadRiffTag(Stream stream)
    {
        var header = new byte[4];
        var read = stream.Read(header, 0, header.Length);
        stream.Position = 0;
        return read == 4 ? Encoding.ASCII.GetString(header) : string.Empty;
    }
}
