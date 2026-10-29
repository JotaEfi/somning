namespace Somning.Core.Overlay;

/// <summary>
/// Damped spring toward a target (semi-implicit Euler with fixed substeps, stable at any frame rate).
/// An under-damped spring overshoots, which gives the orb its elastic entrance.
/// </summary>
public sealed class Spring
{
    private const float SubStepSeconds = 1f / 240f;

    public Spring(float stiffness, float damping, float value = 0f)
    {
        Stiffness = stiffness;
        Damping = damping;
        Value = value;
        Target = value;
    }

    public float Stiffness { get; set; }
    public float Damping { get; set; }
    public float Value { get; private set; }
    public float Velocity { get; private set; }
    public float Target { get; set; }

    /// <summary>Damping ratio: below 1 the spring overshoots.</summary>
    public float DampingRatio => Damping / (2f * MathF.Sqrt(Stiffness));

    public void Reset(float value)
    {
        Value = value;
        Velocity = 0f;
        Target = value;
    }

    public void Step(float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;
        var steps = Math.Max(1, (int)MathF.Ceiling(deltaSeconds / SubStepSeconds));
        var h = deltaSeconds / steps;
        for (var i = 0; i < steps; i++)
        {
            var accel = Stiffness * (Target - Value) - Damping * Velocity;
            Velocity += accel * h;
            Value += Velocity * h;
        }
    }
}
