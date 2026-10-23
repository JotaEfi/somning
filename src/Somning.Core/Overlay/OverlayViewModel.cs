namespace Somning.Core.Overlay;

/// <summary>Immutable snapshot consumed by the overlay renderer. Build it through the factories.</summary>
public sealed record OverlayViewModel
{
    public OverlayViewModel()
    {
        Bars = [];
    }

    public OverlayViewModel(OverlayMode mode, string? text = null, float[]? bars = null, OverlayNoticeKind? notice = null)
    {
        Mode = mode;
        Text = text;
        Bars = bars?.ToArray() ?? [];
        Notice = notice;
    }

    public OverlayMode Mode { get; init; } = OverlayMode.Hidden;
    public string? Text { get; init; }

    /// <summary>Alias for bindings that name the line "message".</summary>
    public string? Message => Text;

    public float[] Bars { get; init; }

    public OverlayNoticeKind? Notice { get; init; }

    /// <summary>Alias for consumers that name the notice reason explicitly.</summary>
    public OverlayNoticeKind? NoticeKind => Notice;

    public static OverlayViewModel CreateHidden() => new(OverlayMode.Hidden);

    public static OverlayViewModel CreateListening(float[] bars) =>
        new(OverlayMode.Listening, OverlayText.Listening, bars);

    public static OverlayViewModel CreateTranscribing() =>
        new(OverlayMode.Transcribing, OverlayText.Transcribing);

    public static OverlayViewModel CreateNotice(OverlayNoticeKind kind) =>
        new(OverlayMode.Notice, OverlayText.For(kind), notice: kind);
}
