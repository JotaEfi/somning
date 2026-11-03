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
    public async Task Classic_theme_plays_synthesized_wav_cues_instead_of_the_console_beep()
    {
        var played = 0;
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new SoundCuePlayer(stream =>
        {
            if (Interlocked.Increment(ref played) == 2) completion.TrySetResult(ReadRiffTag(stream));
        });

        Assert.Equal(SoundThemes.Classic, player.Theme);
        player.PlayStart();
        player.PlayCancel();

        Assert.Equal("RIFF", await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Tone_ends_on_silence_so_the_cue_cannot_click()
    {
        var wav = CueWave.Tone(SoundCuePlayer.StartFrequency, SoundCuePlayer.ShortCueMs);

        var lastSample = BitConverter.ToInt16(wav, wav.Length - 2);
        var lastToneSample = BitConverter.ToInt16(wav, 44 + (CueWave.SampleRate * SoundCuePlayer.ShortCueMs / 1000 - 1) * 2);
        Assert.Equal(0, lastSample);
        Assert.True(Math.Abs((int)lastToneSample) < 200);
    }

    [Fact]
    public void Every_embedded_wav_is_smoothed_to_end_on_silence()
    {
        var assembly = typeof(SoundCuePlayer).Assembly;
        foreach (var (id, _) in SoundThemes.All.Where(theme => theme.Id != SoundThemes.Classic))
        {
            foreach (var cue in Enum.GetValues<SoundCue>())
            {
                using var stream = assembly.GetManifestResourceStream(SoundThemes.ResourceName(id, cue))!;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var smoothed = CueWave.Smooth(copy.ToArray());
                Assert.Equal(0, BitConverter.ToInt16(smoothed, smoothed.Length - 2));
                Assert.True(smoothed.Length > copy.Length);
            }
        }
    }

    [Fact]
    public void Embedded_wavs_have_their_abrupt_decay_step_removed()
    {
        var assembly = typeof(SoundCuePlayer).Assembly;
        foreach (var (id, _) in SoundThemes.All.Where(theme => theme.Id != SoundThemes.Classic))
        {
            foreach (var cue in Enum.GetValues<SoundCue>())
            {
                using var stream = assembly.GetManifestResourceStream(SoundThemes.ResourceName(id, cue))!;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var original = ToSamples(copy.ToArray());
                var step = CueWave.FindStep(original);
                Assert.True(step > 0, $"{id}/{cue}: the shipped file is expected to contain a step");

                var smoothed = ToSamples(CueWave.Smooth(copy.ToArray()));
                Assert.All(smoothed.Skip(step), sample => Assert.Equal(0, sample));
                var from = Math.Max(1, step - CueWave.SampleRate / 100);
                var largestJump = Enumerable.Range(from, smoothed.Length - from).Max(i => Math.Abs(smoothed[i] - smoothed[i - 1]));
                var originalJump = Enumerable.Range(from, original.Length - from).Max(i => Math.Abs(original[i] - original[i - 1]));
                Assert.True(largestJump < 150 && largestJump <= originalJump, $"{id}/{cue}: jump {largestJump} (was {originalJump})");
            }
        }
    }

    private static short[] ToSamples(byte[] wav)
    {
        var samples = new short[(wav.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(wav, 44 + i * 2);
        return samples;
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
