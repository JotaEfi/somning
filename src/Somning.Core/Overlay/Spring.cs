namespace Somning.Core.Overlay;

/// <summary>
/// Damped harmonic oscillator used for the elastic, jelly-like motion of the drop. Integrated with a fixed
/// sub-step so that stiff springs stay stable at any frame time. Allocation free.
/// </summary>
public struct Spring
{
    private const float MaxSubStepSeconds = 1f / 240f;

    public float Value;
    public float Velocity;

    public Spring(float value)
    {
        Value = value;
        Velocity = 0f;
    }

    /// <summary>Advances toward <paramref name="target"/> with angular frequency <paramref name="omega"/> and damping ratio <paramref name="zeta"/>.</summary>
    public void Step(float target, float omega, float zeta, float deltaSeconds)
    {
        if (deltaSeconds <= 0f) return;
        var steps = (int)MathF.Ceiling(deltaSeconds / MaxSubStepSeconds);
        var h = deltaSeconds / steps;
        for (var i = 0; i < steps; i++)
        {
            var acceleration = omega * omega * (target - Value) - 2f * zeta * omega * Velocity;
            Velocity += acceleration * h;
            Value += Velocity * h;
        }
    }

    public void Reset(float value)
    {
        Value = value;
        Velocity = 0f;
    }
}
