namespace Somning.Core.Overlay;

/// <summary>
/// Pure animation state for the glass drop. The platform layer feeds it mode changes and the live voice level,
/// calls <see cref="Tick"/> once per frame and draws <see cref="Frame"/>. No per-frame allocation. Not thread-safe:
/// use it from the render thread only.
/// </summary>
public sealed class OverlayAnimator
{
    public const float MaxDeltaSeconds = 0.1f;
    public const float EnterFadeSeconds = 0.12f;
    public const float ExitFadeSeconds = 0.24f;
    public const float BirthScale = 0.08f;
    public const float ExitScale = 0.55f;
    public const float PaletteSeconds = 0.4f;

    private const float TwoPi = MathF.PI * 2f;
    private const float EggBulge = 0.035f;
    private const float MaxStretch = 0.06f;   // contained squash/stretch: lively, not rubbery

    private readonly VoiceLevelNormalizer normalizer = new();
    private readonly DropStretch dropStretch = new();
    private float drive;
    private Spring scale = new(BirthScale);
    private Spring radius = new(1f);
    private Spring stretch = new(0f);
    private Spring morph = new(0f);
    private Spring amp2 = new(0f);
    private Spring amp3 = new(0f);
    private Spring amp4 = new(0f);
    private float phase2, phase3, phase4;
    private float flowTime;
    private float seconds;
    private float fade;
    private float rawLevel;
    private float level;
    private float transcribeMix;
    private float noticeMix;
    private bool isShowing;
    private bool isHidden = true;
    private OverlayMode mode = OverlayMode.Hidden;
    private DropFrame frame = DropFrame.Static(OverlayMode.Hidden);

    public DropFrame Frame => frame;

    /// <summary>
    /// True when nothing is moving fast (fully shown, settled mode, quiet voice, no pop or stretch): the window may be
    /// redrawn at a lower rate (about 30 fps) without visible judder, halving CPU use during silence, transcribing and notices.
    /// </summary>
    public bool IsCalm =>
        !isHidden && isShowing && fade >= 1f && level < 0.04f && MathF.Abs(scale.Value - 1f) < 0.01f && MathF.Abs(scale.Velocity) < 0.08f
        && MathF.Abs(morph.Value - (mode == OverlayMode.Notice ? 1f : 0f)) < 0.01f
        && MathF.Abs(transcribeMix - (mode == OverlayMode.Transcribing ? 1f : 0f)) < 0.01f
        && MathF.Abs(noticeMix - (mode == OverlayMode.Notice ? 1f : 0f)) < 0.01f
        && dropStretch.Elongation is < 0.12f and > -0.05f;

    /// <summary>True while the window must keep being redrawn (visible or fading out).</summary>
    public bool IsActive => !isHidden;

    public void Show(OverlayMode newMode)
    {
        if (newMode == OverlayMode.Hidden)
        {
            Hide();
            return;
        }

        if (isHidden)
        {
            scale.Reset(BirthScale);
            radius.Reset(1f);
            stretch.Reset(0f);
            amp2.Reset(0f);
            amp3.Reset(0f);
            amp4.Reset(0f);
            morph.Reset(newMode == OverlayMode.Notice ? 1f : 0f);
            transcribeMix = newMode == OverlayMode.Transcribing ? 1f : 0f;
            noticeMix = newMode == OverlayMode.Notice ? 1f : 0f;
            seconds = 0f;
            flowTime = 0f;
            fade = 0f;
            level = 0f;
            rawLevel = 0f;
            normalizer.Reset();
            dropStretch.Reset();
            drive = 0f;
            isHidden = false;
        }

        mode = newMode;
        isShowing = true;
        Refresh();
    }

    public void Hide()
    {
        isShowing = false;
        Refresh();
    }

    /// <summary>Latest raw voice level from the level tracker (0..1). Only used while listening.</summary>
    public void SetLevel(float rawVoiceLevel) => rawLevel = Math.Clamp(rawVoiceLevel, 0f, 1f);

    /// <summary>Advances the animation; returns true while more frames are needed.</summary>
    public bool Tick(float deltaSeconds)
    {
        if (isHidden) return false;
        var dt = Math.Clamp(deltaSeconds, 0f, MaxDeltaSeconds);
        seconds += dt;

        StepVisibility(dt);
        StepVoice(dt);
        StepPalette(dt);
        StepShape(dt);
        Refresh();
        return IsActive;
    }

    private void StepVisibility(float dt)
    {
        if (isShowing)
        {
            fade = Math.Min(1f, fade + dt / EnterFadeSeconds);
            scale.Step(1f, 19f, 0.46f, dt);
            return;
        }

        fade = Math.Max(0f, fade - dt / ExitFadeSeconds);
        scale.Step(ExitScale, 15f, 1f, dt);
        if (fade <= 0f)
        {
            isHidden = true;
            scale.Reset(BirthScale);
        }
    }

    private void StepVoice(float dt)
    {
        drive = mode == OverlayMode.Listening && isShowing
            ? normalizer.Update(rawLevel, dt)
            : 0f;
        var previous = level;
        var tau = drive > level ? 0.032f : 0.15f;
        level += (drive - level) * (1f - MathF.Exp(-dt / tau));

        // Onsets kick the squash/stretch spring so words "hit" the drop.
        stretch.Velocity += (level - previous) * 2.4f;
    }

    private void StepPalette(float dt)
    {
        var blend = 1f - MathF.Exp(-dt / (PaletteSeconds * 0.35f));
        transcribeMix += ((mode == OverlayMode.Transcribing ? 1f : 0f) - transcribeMix) * blend;
        noticeMix += ((mode == OverlayMode.Notice ? 1f : 0f) - noticeMix) * blend;
    }

    private void StepShape(float dt)
    {
        var breathPeriod = mode == OverlayMode.Transcribing ? 1.5f : 2.7f;
        var breathAmount = mode == OverlayMode.Notice ? 0f : mode == OverlayMode.Transcribing ? 0.03f : 0.02f;
        var breath = breathAmount * MathF.Sin(TwoPi * seconds / breathPeriod);
        radius.Step(1f + breath + 0.035f * level, 22f, 0.62f, dt);
        stretch.Step(0f, 19f, 0.55f, dt);
        stretch.Value = Math.Clamp(stretch.Value, -MaxStretch, MaxStretch);
        morph.Step(mode == OverlayMode.Notice ? 1f : 0f, 13f, 0.85f, dt);
        var idle = mode == OverlayMode.Transcribing ? 0.5f + 0.5f * MathF.Sin(TwoPi * seconds / 1.5f) : 0f;
        dropStretch.Update(drive, idle, dt);

        var calm = mode == OverlayMode.Notice ? 0.25f : 1f;
        amp2.Step(calm * (0.008f + 0.014f * level), 22f, 0.65f, dt);
        amp3.Step(calm * (0.006f + 0.010f * level), 24f, 0.65f, dt);
        amp4.Step(calm * (0.003f + 0.005f * level), 26f, 0.65f, dt);

        phase2 = Wrap(phase2 + dt * (0.9f + 2.4f * level));
        phase3 = Wrap(phase3 - dt * (1.3f + 3.0f * level));
        phase4 = Wrap(phase4 + dt * (1.7f + 3.6f * level));

        var flowSpeed = mode switch
        {
            OverlayMode.Transcribing => 1.15f,
            OverlayMode.Notice => 0.3f,
            _ => 0.45f + 1.9f * level
        };
        flowTime += dt * flowSpeed;
    }

    private static float Wrap(float angle) => angle > TwoPi ? angle - TwoPi : angle < -TwoPi ? angle + TwoPi : angle;

    private void Refresh()
    {
        var eased = fade * fade * (3f - 2f * fade);
        var m = Math.Clamp(morph.Value, 0f, 1f);
        var lobeScale = 1f - m;
        var egg = EggBulge * lobeScale;
        var a2 = amp2.Value * lobeScale;
        var a3 = amp3.Value * lobeScale;
        var a4 = amp4.Value * lobeScale;

        frame = new DropFrame(
            mode,
            eased,
            Math.Clamp(scale.Value, 0f, 1.4f),
            flowTime,
            level,
            Math.Clamp(radius.Value, 0.8f, 1.3f),
            Math.Clamp(stretch.Value, -MaxStretch, MaxStretch) * lobeScale,
            m,
            Math.Clamp(transcribeMix, 0f, 1f),
            Math.Clamp(noticeMix, 0f, 1f),
            0f, -egg, // pulls the bottom out slightly: a drop rather than a perfect sphere
            a2 * MathF.Cos(phase2), a2 * MathF.Sin(phase2),
            a3 * MathF.Cos(phase3), a3 * MathF.Sin(phase3),
            a4 * MathF.Cos(phase4), a4 * MathF.Sin(phase4),
            dropStretch.Elongation * lobeScale,
            dropStretch.Lean * lobeScale,
            dropStretch.Ripple * lobeScale,
            dropStretch.RipplePhase);
    }
}
