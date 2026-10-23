namespace Somning.Core.Overlay;

public enum OverlayMode
{
    Hidden,
    Listening,
    Transcribing,
    Notice
}

public enum OverlayNoticeKind
{
    MicMissing,
    MicBlocked,
    SilenceOnly,
    NothingDetected,
    ModelMissing
}

/// <summary>Single source of user-facing overlay text (APP-05, pt-BR).</summary>
public static class OverlayText
{
    public const string Listening = "Ouvindo...";
    public const string Transcribing = "Transcrevendo...";

    public static string For(OverlayNoticeKind kind) => kind switch
    {
        OverlayNoticeKind.MicMissing => "Microfone não encontrado",
        OverlayNoticeKind.MicBlocked => "Microfone bloqueado",
        OverlayNoticeKind.SilenceOnly => "Apenas silêncio detectado",
        OverlayNoticeKind.NothingDetected => "Nenhuma fala detectada",
        OverlayNoticeKind.ModelMissing => "Modelo de transcrição ausente",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown overlay notice kind.")
    };
}
