namespace Somning.Core.Overlay;

/// <summary>
/// Turns raw microphone RMS (typically 0.01-0.2 for normal speech, 0.001-0.005 for room noise) into a 0..1 visual
/// level. Works in dBFS with an adaptive noise floor and an adaptive ceiling, so a quiet voice and a loud voice both
/// move the orb visibly while room noise stays near zero. One instance per recording.
/// </summary>
public sealed class VoiceLevelNormalizer
{
    public const float InitialFloorDb = -58f;
    public const float MinFloorDb = -66f;
    public const float MaxFloorDb = -46f;
    public const float FloorMarginDb = 4f;
    public const float InitialCeilingDb = -24f;
    public const float MinCeilingDb = -32f;
    public const float CeilingDecayDbPerSecond = 2f;
    public const float FloorRiseSeconds = 40f;

    private float floorDb = InitialFloorDb;
    private float ceilingDb = InitialCeilingDb;

    public float FloorDb => floorDb;
    public float CeilingDb => ceilingDb;

    public void Reset()
    {
        floorDb = InitialFloorDb;
        ceilingDb = InitialCeilingDb;
    }

    /// <summary>Maps one RMS measurement (spanning <paramref name="deltaSeconds"/> of audio) to a level in [0, 1].</summary>
    public float Normalize(float rms, float deltaSeconds)
    {
        var dt = Math.Clamp(deltaSeconds, 0.001f, 0.25f);
        var db = 20f * MathF.Log10(Math.Max(rms, 1e-5f));

        // Noise floor: drops immediately to quieter audio, creeps up very slowly (never follows sustained speech).
        floorDb = db < floorDb
            ? floorDb + (db - floorDb) * 0.5f
            : floorDb + (db - floorDb) * Math.Min(1f, dt / FloorRiseSeconds);
        floorDb = Math.Clamp(floorDb, MinFloorDb, MaxFloorDb);

        // Ceiling: jumps to louder audio, decays slowly back toward a normal-speech reference.
        ceilingDb = Math.Max(db, ceilingDb - CeilingDecayDbPerSecond * dt);
        ceilingDb = Math.Max(ceilingDb, MinCeilingDb);

        var low = floorDb + FloorMarginDb;
        var span = Math.Max(ceilingDb - low, 12f);
        return Math.Clamp((db - low) / span, 0f, 1f);
    }
}
