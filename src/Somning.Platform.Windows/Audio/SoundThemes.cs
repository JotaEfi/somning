namespace Somning.Platform.Windows.Audio;

/// <summary>Available sound themes. Ids are stored in settings; display names are shown in the settings window.</summary>
public static class SoundThemes
{
    /// <summary>The original Windows beep cues, kept as the default.</summary>
    public const string Classic = "classico";
    public const string Drop = "gota";
    public const string Glass = "vidro";
    public const string Wood = "madeira";

    public static IReadOnlyList<(string Id, string DisplayName)> All { get; } =
    [
        (Classic, "Bipe padrão"),
        (Drop, "Gota d'água (suave)"),
        (Glass, "Vidro suave"),
        (Wood, "Madeira"),
    ];

    public static bool IsKnown(string? id) => All.Any(theme => theme.Id == id);

    /// <summary>Manifest resource name of the WAV for a theme and cue, from the Audio/Sounds folder.</summary>
    public static string ResourceName(string theme, SoundCue cue) =>
        $"Somning.Platform.Windows.Audio.Sounds.{theme}.{cue.ToString().ToLowerInvariant()}.wav";
}
