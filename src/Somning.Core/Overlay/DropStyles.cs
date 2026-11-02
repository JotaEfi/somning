namespace Somning.Core.Overlay;

/// <summary>
/// Visual styles of the glass drop. Each style is a pair: the listening drop and the matching transcribing drop
/// (the same bubble with its calm palette and swirl), so a new style always ships both. Ids are stored in settings;
/// display names are shown in the settings window. Unknown ids fall back to <see cref="Classic"/>.
/// </summary>
public static class DropStyles
{
    /// <summary>The original graphite glass drop with the dark reactive wave band, kept as the default.</summary>
    public const string Classic = "classico";

    public const string C = "c";
    public const string F = "f";
    public const string G = "g";
    public const string H = "h";

    public static IReadOnlyList<(string Id, string DisplayName)> All { get; } =
    [
        (Classic, "Clássica (grafite)"),
        (C, "Gel reativo"),
        (F, "Mercúrio"),
        (G, "Lente líquida"),
        (H, "Água escura"),
    ];

    public static bool IsKnown(string? id) => All.Any(style => style.Id == id);

    /// <summary>The id itself when known, otherwise <see cref="Classic"/>.</summary>
    public static string Normalize(string? id) => IsKnown(id) ? id! : Classic;
}
