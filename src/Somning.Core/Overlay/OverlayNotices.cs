using Somning.Core.Cloud;

namespace Somning.Core.Overlay;

/// <summary>Maps a failed transcription's error code to the pt-BR notice shown by the overlay (APP-05).</summary>
public static class OverlayNotices
{
    /// <summary>The cloud notice for <paramref name="errorCode"/>, or <c>null</c> for local/unclassified errors.</summary>
    public static OverlayNoticeKind? ForErrorCode(string? errorCode) => errorCode switch
    {
        CloudErrorCodes.NoNetwork => OverlayNoticeKind.CloudNoNetwork,
        CloudErrorCodes.InvalidApiKey => OverlayNoticeKind.CloudInvalidKey,
        CloudErrorCodes.RateLimit => OverlayNoticeKind.CloudRateLimit,
        CloudErrorCodes.Timeout => OverlayNoticeKind.CloudTimeout,
        _ => null
    };

    /// <summary>Error code reported when the transcription ceiling elapsed without an answer from the engine.</summary>
    public const string TranscribeTimeoutCode = "transcribe_timeout";

    /// <summary>
    /// Notice for any failed transcription: cloud codes keep their specific text, a timeout says so, and every other
    /// failure (engine down, crashed, unclassified) gets the generic recoverable-failure notice instead of silence.
    /// </summary>
    public static OverlayNoticeKind ForFailure(string? errorCode) =>
        ForErrorCode(errorCode)
        ?? (errorCode == TranscribeTimeoutCode ? OverlayNoticeKind.TranscribeTimeout : OverlayNoticeKind.EngineFailed);
}
