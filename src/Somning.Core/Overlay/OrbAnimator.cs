namespace Somning.Core.Overlay;

/// <summary>
/// Pure animation state of the liquid orb: elastic entrance, dissolving exit, voice-driven spring, breathing in
/// silence and palette crossfades. Not thread-safe (render thread only); no per-frame allocation.
/// </summary>
public sealed class OrbAnimator
{
    public const float MaxDeltaSeconds = 0.1f;
    public const float EntranceStartScale = 0.12f;
    public const float ExitScale = 0.3f;
    public const float BreathPeriodSeconds = 3.4f;
    public const float HiddenOpacity = 0.01f;

    private const float EnterStiffness = 250f;
    private const float EnterDamping = 15f;
    private const float ExitStiffness = 170f;

    private readonly Spring scale = new(EnterStiffness, EnterDamping, EntranceStartScale);
    private readonly Spring level = new(190f, 15f);
    private OverlayMode mode = OverlayMode.Hidden;
    private bool isShowing;
    private bool isVisible;
    private float opacity;
    private float inputLevel;
    private float flow;
    private float swirl;
    private float time;
    private float transcribeMix;
    private float noticeMix;
    private OrbFrame frame = OrbFrame.Static(OverlayMode.Hidden);

    public OrbFrame Frame => frame;

    /// <summary>True while the window must keep being redrawn (visible, entering or dissolving).</summary>
    public bool IsActive => isShowing || isVisible;

    /// <summary>Entrance spring is under-damped so the drop overshoots before settling.</summary>
    public float EntranceDampingRatio => EnterDamping / (2f * MathF.Sqrt(EnterStiffness));

    public void Show(OverlayMode newMode)
    {
        if (newMode == OverlayMode.Hidden)
        {
            Hide();
            return;
        }

        if (!isVisible)
        {
            scale.Reset(EntranceStartScale);
            level.Reset(0f);
            opacity = 0f;
            flow = 0f;
            transcribeMix = newMode == OverlayMode.Transcribing ? 1f : 0f;
            noticeMix = newMode == OverlayMode.Notice ? 1f : 0f;
        }

        mode = newMode;
        isShowing = true;
        isVisible = true;
        scale.Stiffness = EnterStiffness;
        scale.Damping = EnterDamping;
        scale.Target = 1f;
        Refresh();
    }

    public void Hide()
    {
        if (!isShowing) return;
        isShowing = false;
        scale.Stiffness = ExitStiffness;
        scale.Damping = 2f * MathF.Sqrt(ExitStiffness); // critically damped shrink: no bounce while dissolving
        scale.Target = ExitScale;
        Refresh();
    }

    /// <summary>Normalized voice level in [0, 1] (already adaptive-gain corrected).</summary>
    public void SetLevel(float value) => inputLevel = OrbMath.Clamp01(value);

    /// <summary>Advances the animation; returns true while more frames are needed.</summary>
    public bool Tick(float deltaSeconds)
    {
        var dt = Math.Clamp(deltaSeconds, 0f, MaxDeltaSeconds);
        time += dt;

        scale.Step(dt);
        level.Target = LevelTarget();
        level.Step(dt);

        var speed = mode switch
        {
            OverlayMode.Listening => 0.55f + 2.4f * OrbMath.Clamp01(level.Value),
            OverlayMode.Transcribing => 0.9f,
            _ => 0.6f
        };
        flow += dt * speed;
        swirl += dt * (mode == OverlayMode.Transcribing ? 1.4f : 0.35f);

        var targetOpacity = isShowing ? 1f : 0f;
        var tau = isShowing ? 0.07f : 0.09f;
        opacity += (targetOpacity - opacity) * (1f - MathF.Exp(-dt / tau));
        if (!isShowing && opacity < HiddenOpacity)
        {
            opacity = 0f;
            isVisible = false;
        }

        var mixStep = 1f - MathF.Exp(-dt / 0.18f);
        transcribeMix += ((mode == OverlayMode.Transcribing ? 1f : 0f) - transcribeMix) * mixStep;
        noticeMix += ((mode == OverlayMode.Notice ? 1f : 0f) - noticeMix) * mixStep;

        Refresh();
        return IsActive;
    }

    private float LevelTarget() => mode switch
    {
        OverlayMode.Listening => inputLevel,
        OverlayMode.Transcribing => 0.2f + 0.08f * MathF.Sin(time * 2.1f),
        OverlayMode.Notice => 0.14f + 0.1f * MathF.Sin(time * 4.2f),
        _ => 0f
    };

    private void Refresh()
    {
        var lv = OrbMath.Clamp01(level.Value);
        var breath = MathF.Sin(time * 2f * MathF.PI / BreathPeriodSeconds) * (1f - lv);
        var soften = OrbMath.Clamp01(1f - opacity);
        frame = new OrbFrame(mode, opacity, Math.Max(0f, scale.Value), lv, flow, swirl, breath, soften, transcribeMix, noticeMix);
    }
}
