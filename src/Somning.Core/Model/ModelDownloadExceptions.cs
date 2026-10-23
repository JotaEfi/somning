namespace Somning.Core.Model;

/// <summary>The downloaded bytes did not match the manifest hash; the corrupt <c>.part</c> was deleted.</summary>
public sealed class ModelChecksumMismatchException : Exception
{
    public ModelChecksumMismatchException(string expected, string actual)
        : base($"Model SHA-256 mismatch: expected '{expected}', got '{actual}'.")
    {
        Expected = expected;
        Actual = actual;
    }

    public string Expected { get; }

    public string Actual { get; }
}

/// <summary>
/// A download attempt failed. <see cref="IsRetryable"/> mirrors <see cref="ModelDownloadManager.IsRetryable(Exception)"/>
/// so an upper layer can schedule another attempt without re-inspecting the inner exception.
/// </summary>
public sealed class ModelDownloadFailedException : Exception
{
    public ModelDownloadFailedException(string message, bool isRetryable, Exception? inner = null)
        : base(message, inner) => IsRetryable = isRetryable;

    public bool IsRetryable { get; }
}
