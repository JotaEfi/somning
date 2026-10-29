namespace Somning.Core.Overlay;

/// <summary>
/// Pure animation state for the pill. The platform layer feeds it mode changes and live bars, calls
/// <see cref="Tick"/> once per frame and draws <see cref="Frame"/> + <see cref="Bars"/>. No per-frame allocation.
/// Not thread-safe: use it from the render thread only.
/// </summary>
public sealed class OverlayAnimator
{
    public const int DefaultBarCount = 5;
    public const float ListeningPulsePeriodSeconds = 1.6f;
    public const float TranscribingPulsePeriodSeconds = 0.9f;
    public const float MaxDeltaSeconds = 0.1f;

    private readonly OverlayTransition transition = new();
    private BarSmoother smoother = new(DefaultBarCount);
    private float[] live = new float[DefaultBarCount];
    private float[] targets = new float[DefaultBarCount];
    private int liveCount;
    private float seconds;
    private OverlayMode mode = OverlayMode.Hidden;
    private OverlayFrame frame = OverlayFrame.Static(OverlayMode.Hidden);

    public OverlayFrame Frame => frame;

    public ReadOnlySpan<float> Bars => smoother.Values;

    /// <summary>True while the window must keep being redrawn (visible or fading).</summary>
    public bool IsActive => !transition.IsHidden;

    public void Show(OverlayMode newMode)
    {
        if (newMode == OverlayMode.Hidden)
        {
            Hide();
            return;
        }

        if (transition.IsHidden)
        {
            smoother.Reset(AudioLevelTracker.MinimumBarHeight);
            liveCount = 0;
        }

        mode = newMode;
        transition.Show();
        Refresh();
    }

    public void Hide()
    {
        transition.Hide();
        Refresh();
    }

    /// <summary>Latest bars from the audio level tracker (values in [0, 1]); copied, the caller keeps its array.</summary>
    public void SetLiveBars(ReadOnlySpan<float> bars)
    {
        if (bars.Length == 0)
        {
            liveCount = 0;
            return;
        }

        if (bars.Length != smoother.Count) Resize(bars.Length);
        bars.CopyTo(live);
        liveCount = bars.Length;
    }

    /// <summary>Advances the animation; returns true while more frames are needed.</summary>
    public bool Tick(float deltaSeconds)
    {
        var dt = Math.Clamp(deltaSeconds, 0f, MaxDeltaSeconds);
        seconds += dt;
        transition.Advance(dt);
        ComputeTargets();
        smoother.Update(targets, dt);
        Refresh();
        return IsActive;
    }

    private void ComputeTargets()
    {
        var count = smoother.Count;
        for (var i = 0; i < count; i++)
        {
            targets[i] = mode switch
            {
                OverlayMode.Listening => ListeningTarget(i),
                OverlayMode.Transcribing => OverlayWaves.Shimmer(i, count, seconds),
                _ => AudioLevelTracker.MinimumBarHeight
            };
        }
    }

    private float ListeningTarget(int index)
    {
        var idle = OverlayWaves.Idle(index, seconds);
        if (index >= liveCount) return idle;
        // A touch of per-bar ripple so loud speech does not look like a rigid, symmetric block.
        var ripple = 0.94f + 0.06f * MathF.Sin(seconds * 9f + index * 1.7f);
        return Math.Max(idle, live[index] * ripple);
    }

    private void Refresh()
    {
        var period = mode == OverlayMode.Transcribing ? TranscribingPulsePeriodSeconds : ListeningPulsePeriodSeconds;
        frame = new OverlayFrame(mode, transition.Opacity, transition.Scale, OverlayWaves.Pulse(seconds, period), CurrentEnergy());
    }

    private float CurrentEnergy()
    {
        if (mode != OverlayMode.Listening) return 0f;
        var peak = 0f;
        foreach (var value in smoother.Values) peak = Math.Max(peak, value);
        var rest = AudioLevelTracker.MinimumBarHeight + OverlayWaves.IdleAmplitude;
        return Math.Clamp((peak - rest) / (1f - rest), 0f, 1f);
    }

    private void Resize(int count)
    {
        smoother = new BarSmoother(count);
        smoother.Reset(AudioLevelTracker.MinimumBarHeight);
        live = new float[count];
        targets = new float[count];
        liveCount = 0;
    }
}
