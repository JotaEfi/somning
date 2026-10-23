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
}
