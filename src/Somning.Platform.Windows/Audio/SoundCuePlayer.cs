using System.IO;
using System.Media;
using System.Reflection;

namespace Somning.Platform.Windows.Audio;

/// <summary>Capture start/stop/done/cancel feedback; silent when cues are disabled in settings (OVL-04).</summary>
public interface ISoundCuePlayer
{
    void PlayStart();
    void PlayStop();
    void PlayDone();
    void PlayCancel();
}

/// <summary>Named cue sounds; each sound theme ships one WAV per cue, embedded in the assembly.</summary>
public enum SoundCue
{
    Start,
    Stop,
    Done,
    Cancel,
}

/// <summary>
/// Plays the cues of the selected theme on the thread pool. Every cue, including the classic beeps, is a WAV played
/// through a persistent output stream (see <see cref="CueOutput"/>): <c>Console.Beep</c> is silent in a windowless app on many machines. Cues are
/// conditioned (fade-out and silent tail) so they never end in a click, and are mixed into a stream that never closes, so the
/// device never pops. Capturing audio never waits for a cue, and a machine whose audio stack rejects the sound
/// simply stays silent instead of failing the dictation.
/// </summary>
public sealed class SoundCuePlayer : ISoundCuePlayer, IDisposable
{
    public const string DefaultTheme = SoundThemes.Classic;

    // Classic beep cues: the original tones used before the WAV themes existed.
    public const int StartFrequency = 880;
    public const int StopFrequency = 660;
    public const int DoneFrequency = 1046;
    public const int CancelFrequency = 440;
    public const int ShortCueMs = 80;
    public const int CancelCueMs = 140;

    private readonly Action<Stream> play;
    private readonly CueOutput? sharedOutput;
    private volatile bool enabled = true;
    private volatile string theme = DefaultTheme;

    public SoundCuePlayer() : this(null) { }

    /// <summary>The playback delegate is injectable so tests can observe cues without touching the audio device.</summary>
    public SoundCuePlayer(Action<Stream>? play)
    {
        if (play is null)
        {
            sharedOutput = new CueOutput();
            this.play = PlayWithFallback;
        }
        else
        {
            this.play = play;
        }
    }

    public void Dispose() => sharedOutput?.Dispose();

    /// <summary>The persistent stream is preferred; if it cannot open for any reason the cue still plays the classic way.</summary>
    private void PlayWithFallback(Stream wav)
    {
        var bytes = new MemoryStream();
        wav.CopyTo(bytes);
        try
        {
            bytes.Position = 0;
            sharedOutput!.Play(bytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            bytes.Position = 0;
            using var player = new SoundPlayer(bytes);
            player.PlaySync();
        }
    }

    public bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    /// <summary>Theme id from <see cref="SoundThemes"/>; unknown ids fall back to the default theme.</summary>
    public string Theme
    {
        get => theme;
        set => theme = SoundThemes.IsKnown(value) ? value : DefaultTheme;
    }

    public void PlayStart() => Play(SoundCue.Start);

    public void PlayStop() => Play(SoundCue.Stop);

    public void PlayDone() => Play(SoundCue.Done);

    public void PlayCancel() => Play(SoundCue.Cancel);

    private void Play(SoundCue cue)
    {
        if (!enabled) return;

        var selected = theme;
        _ = Task.Run(() =>
        {
            try
            {
                var wav = selected == SoundThemes.Classic ? ClassicWav(cue) : EmbeddedWav(SoundThemes.ResourceName(selected, cue));
                if (wav is null) return;
                play(new MemoryStream(wav));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or PlatformNotSupportedException or TimeoutException)
            {
                // Cues are cosmetic: a broken audio stack must never fail a dictation.
            }
        });
    }

    private static byte[] ClassicWav(SoundCue cue) => cue switch
    {
        SoundCue.Start => CueWave.Tone(StartFrequency, ShortCueMs),
        SoundCue.Stop => CueWave.Tone(StopFrequency, ShortCueMs),
        SoundCue.Done => CueWave.Tone(DoneFrequency, ShortCueMs),
        _ => CueWave.Tone(CancelFrequency, CancelCueMs),
    };

    private static byte[]? EmbeddedWav(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return CueWave.Smooth(copy.ToArray());
    }
}
