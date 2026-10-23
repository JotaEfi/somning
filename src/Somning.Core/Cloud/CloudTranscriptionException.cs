namespace Somning.Core.Cloud;

/// <summary>Stable error codes for cloud transcription failures (STT-06).</summary>
public static class CloudErrorCodes
{
    /// <summary>DNS failure, connection refused or the request never reached the provider.</summary>
    public const string NoNetwork = "no_network";

    /// <summary>HTTP 401: the key is missing, revoked or wrong for this provider.</summary>
    public const string InvalidApiKey = "invalid_api_key";

    /// <summary>HTTP 429: the provider throttled the account.</summary>
    public const string RateLimit = "rate_limit";

    /// <summary>HTTP 408, the client timeout, or a cancelled request.</summary>
    public const string Timeout = "timeout";

    /// <summary>HTTP 400 (or any other 4xx): the provider rejected the payload.</summary>
    public const string BadRequest = "bad_request";

    /// <summary>HTTP 5xx: the provider failed while processing an otherwise valid request.</summary>
    public const string ServerError = "server_error";

    /// <summary>True when the same recording can be retried without user intervention.</summary>
    public static bool IsRetryable(string? errorCode) => errorCode is
        NoNetwork or RateLimit or Timeout or ServerError;
}

/// <summary>
/// A cloud transcription attempt failed. <see cref="ErrorCode"/> is one of <see cref="CloudErrorCodes"/>;
/// the message is technical and never contains the plaintext API key (APP-06). The recording that was
/// uploaded is untouched, so the caller can retry it (STT-06).
/// </summary>
public sealed class CloudTranscriptionException : Exception
{
    public CloudTranscriptionException(
        string errorCode,
        string message,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
            throw new ArgumentException("An error code is required.", nameof(errorCode));

        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    /// <summary>One of <see cref="CloudErrorCodes"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>HTTP status when the failure came from a response; <c>null</c> for transport failures.</summary>
    public int? StatusCode { get; }

    /// <summary>Whether the same recording is worth retrying.</summary>
    public bool IsRetryable => CloudErrorCodes.IsRetryable(ErrorCode);
}
