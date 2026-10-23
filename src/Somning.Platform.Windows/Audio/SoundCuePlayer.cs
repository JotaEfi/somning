namespace Somning.Platform.Windows.Audio;

/// <summary>Capture start/stop/cancel feedback; silent when cues are disabled in settings (OVL-04).</summary>
public interface ISoundCuePlayer
{
    void PlayStart();
    void PlayStop();
    void PlayCancel();
}

/// <summary>
/// Plays short Windows beeps on the thread pool. Capturing audio never waits for the cue, and a machine whose
/// audio stack rejects the beep simply stays silent instead of failing the dictation.
/// </summary>
public sealed class SoundCuePlayer : ISoundCuePlayer
{
    public const int StartFrequency = 880;
    public const int StopFrequency = 660;
    public const int CancelFrequency = 440;
    public const int ShortCueMs = 80;
    public const int CancelCueMs = 140;

    private readonly Action<int, int> beep;
    private volatile bool enabled = true;

    public SoundCuePlayer() : this(null) { }

    /// <summary><paramref name="beep"/> is injectable so tests can observe cues without touching the audio device.</summary>
    public SoundCuePlayer(Action<int, int>? beep) => this.beep = beep ?? Console.Beep;

    public bool Enabled
    {
        get => enabled;
        set => enabled = value;
    }

    public void PlayStart() => Play(StartFrequency, ShortCueMs);

    public void PlayStop() => Play(StopFrequency, ShortCueMs);

    public void PlayCancel() => Play(CancelFrequency, CancelCueMs);

    private void Play(int frequency, int duration)
    {
        if (!enabled) return;

        var play = beep;
        _ = Task.Run(() =>
        {
            try
            {
                play(frequency, duration);
            }
            catch (PlatformNotSupportedException)
            {
                // No beep device: cues are cosmetic, never a reason to fail a capture.
            }
            catch (InvalidOperationException)
            {
                // A concurrent shutdown may close the console beeper.
            }
        });
    }
}
