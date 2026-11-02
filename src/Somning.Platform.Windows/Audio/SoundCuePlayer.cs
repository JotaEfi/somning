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
/// Plays the cues of the selected theme on the thread pool. The classic theme uses the Windows beep; the others play
/// embedded WAV files. Capturing audio never waits for the cue, and a machine whose audio stack rejects the sound
/// simply stays silent instead of failing the dictation.
/// </summary>
public sealed class SoundCuePlayer : ISoundCuePlayer
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
    private readonly Action<int, int> beep;
    private volatile bool enabled = true;
    private volatile string theme = DefaultTheme;

    public SoundCuePlayer() : this(null, null) { }

    /// <summary>Both delegates are injectable so tests can observe cues without touching the audio device.</summary>
    public SoundCuePlayer(Action<Stream>? play, Action<int, int>? beep = null)
    {
        this.play = play ?? PlayWav;
        this.beep = beep ?? Console.Beep;
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
        var player = play;
        var tone = beep;
        _ = Task.Run(() =>
        {
            try
            {
                if (selected == SoundThemes.Classic)
                {
                    PlayClassic(cue, tone);
                    return;
                }
                PlayEmbedded(SoundThemes.ResourceName(selected, cue), player);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or PlatformNotSupportedException)
            {
                // Cues are cosmetic: a broken audio stack must never fail a dictation.
            }
        });
    }

    private static void PlayClassic(SoundCue cue, Action<int, int> beep)
    {
        switch (cue)
        {
            case SoundCue.Start: beep(StartFrequency, ShortCueMs); break;
            case SoundCue.Stop: beep(StopFrequency, ShortCueMs); break;
            case SoundCue.Done: beep(DoneFrequency, ShortCueMs); break;
            case SoundCue.Cancel: beep(CancelFrequency, CancelCueMs); break;
        }
    }

    private static void PlayEmbedded(string resource, Action<Stream> play)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null) return;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        play(copy);
    }

    private static void PlayWav(Stream wav)
    {
        using var player = new SoundPlayer(wav);
        player.PlaySync();
    }
}
