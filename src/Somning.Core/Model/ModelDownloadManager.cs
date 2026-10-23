using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Somning.Core.Model;

/// <summary>What to download and the hash the finished file must have (STT-07).</summary>
public sealed record ModelDownloadRequest(Uri DownloadUri, string DestinationPath, string ExpectedSha256);

/// <summary>
/// Downloads a model file into <c>{destination}.part</c>, resuming interrupted transfers with an HTTP
/// <c>Range</c> request, reporting progress, verifying SHA-256 and only then moving the file into place.
/// Network failures are surfaced as <see cref="ModelDownloadFailedException"/>; transient ones are retried
/// automatically and can also be detected with <see cref="IsRetryable(Exception)"/>.
/// </summary>
public sealed class ModelDownloadManager
{
    private const int BufferSize = 81_920;
    private static readonly HttpClient DefaultHttpClient = new() { Timeout = TimeSpan.FromMinutes(30) };

    private readonly HttpClient httpClient;
    private readonly int maxAttempts;
    private readonly TimeSpan retryDelay;

    public ModelDownloadManager(HttpClient? httpClient = null, int maxAttempts = 3, TimeSpan? retryDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        if (retryDelay is { } delay && delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryDelay), delay, "Retry delay cannot be negative.");

        this.httpClient = httpClient ?? DefaultHttpClient;
        this.maxAttempts = maxAttempts;
        this.retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(250);
    }

    /// <summary>Path of the resumable partial file for <paramref name="destinationPath"/>.</summary>
    public static string GetPartialPath(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        return destinationPath + ".part";
    }

    /// <summary>Percentage in <c>[0, 100]</c>, or <c>0</c> when the total size is unknown.</summary>
    public static double ComputePercent(long bytesDownloaded, long totalBytes)
    {
        if (bytesDownloaded <= 0 || totalBytes <= 0) return 0d;
        if (bytesDownloaded >= totalBytes) return 100d;
        return bytesDownloaded * 100d / totalBytes;
    }

    /// <summary>Builds a progress snapshot with the percentage derived from the byte counters.</summary>
    public static ModelDownloadProgress CreateProgress(long bytesDownloaded, long totalBytes, long speedBps) =>
        new(
            Math.Max(0, bytesDownloaded),
            Math.Max(0, totalBytes),
            ComputePercent(bytesDownloaded, totalBytes),
            Math.Max(0, speedBps));

    /// <summary>Lowercase hexadecimal SHA-256 of <paramref name="data"/>.</summary>
    public static string ComputeSha256(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Lowercase hexadecimal SHA-256 of a file, streamed so large models do not load into memory.</summary>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// True for failures worth retrying: HTTP 408/500/502/503/504, timeouts, dropped sockets and truncated
    /// connections. Client errors (404, bad URL) and SHA-256 mismatches are not retryable.
    /// </summary>
    public static bool IsRetryable(Exception? exception) => exception switch
    {
        null => false,
        ModelDownloadFailedException failure => failure.IsRetryable || IsRetryable(failure.InnerException),
        HttpRequestException http => IsRetryableStatus(http.StatusCode) || IsRetryable(http.InnerException),
        TimeoutException => true,
        SocketException => true,
        IOException => true,
        TaskCanceledException canceled => canceled.InnerException is TimeoutException,
        OperationCanceledException => false,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsRetryable),
        _ => false
    };

    public async Task<ModelDownloadProgress> DownloadAsync(
        ModelDownloadRequest request,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateUri(request.DownloadUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExpectedSha256);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadOnceAsync(request, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException exception)
            {
                var timeout = new ModelDownloadFailedException("Model download timed out.", true, exception);
                if (attempt >= maxAttempts) throw timeout;
                await DelayBeforeRetryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ModelChecksumMismatchException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = exception as ModelDownloadFailedException ??
                    new ModelDownloadFailedException($"Model download failed: {exception.Message}", IsRetryable(exception), exception);
                if (attempt >= maxAttempts || !failure.IsRetryable) throw failure;
                await DelayBeforeRetryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public Task<ModelDownloadProgress> DownloadAsync(
        Uri downloadUri,
        string destinationPath,
        string expectedSha256,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        DownloadAsync(new ModelDownloadRequest(downloadUri, destinationPath, expectedSha256), progress, cancellationToken);

    private async Task<ModelDownloadProgress> DownloadOnceAsync(
        ModelDownloadRequest request,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var destinationPath = request.DestinationPath;
        var expectedHash = NormalizeHash(request.ExpectedSha256);
        var directory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var partPath = GetPartialPath(destinationPath);
        if (File.Exists(destinationPath))
        {
            var existingHash = await ComputeSha256Async(destinationPath, cancellationToken).ConfigureAwait(false);
            if (string.Equals(existingHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                var length = new FileInfo(destinationPath).Length;
                var alreadyComplete = CreateProgress(length, length, 0);
                progress?.Report(alreadyComplete);
                return alreadyComplete;
            }
            TryDelete(destinationPath);
        }

        var resumeOffset = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        using var response = await SendAsync(request.DownloadUri, resumeOffset, cancellationToken).ConfigureAwait(false);
        if (resumeOffset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            // The partial file no longer fits the remote resource: restart from zero.
            TryDelete(partPath);
            resumeOffset = 0;
            using var restarted = await SendAsync(request.DownloadUri, 0, cancellationToken).ConfigureAwait(false);
            return await SaveResponseAsync(restarted, partPath, destinationPath, expectedHash, 0, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return await SaveResponseAsync(response, partPath, destinationPath, expectedHash, resumeOffset, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ModelDownloadProgress> SaveResponseAsync(
        HttpResponseMessage response,
        string partPath,
        string destinationPath,
        string expectedHash,
        long resumeOffset,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.PartialContent))
        {
            response.EnsureSuccessStatusCode();
            throw new HttpRequestException(
                $"Unexpected HTTP status {(int)response.StatusCode} while downloading the model.",
                inner: null,
                response.StatusCode);
        }

        var append = response.StatusCode == HttpStatusCode.PartialContent && resumeOffset > 0;
        if (!append) resumeOffset = 0;

        var contentLength = response.Content.Headers.ContentLength;
        var totalBytes = response.Content.Headers.ContentRange?.Length
            ?? (contentLength is > 0 ? resumeOffset + contentLength.Value : 0);

        progress?.Report(CreateProgress(resumeOffset, totalBytes, 0));

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var stopwatch = Stopwatch.StartNew();
        var transferred = 0L;
        var written = resumeOffset;
        try
        {
            await using (var target = new FileStream(
                partPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    transferred += read;
                    written += read;
                    progress?.Report(CreateProgress(written, totalBytes, ComputeSpeed(transferred, stopwatch.Elapsed)));
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (totalBytes > 0 && written < totalBytes)
        {
            throw new ModelDownloadFailedException(
                $"Model download ended early ({written} of {totalBytes} bytes).",
                isRetryable: true);
        }

        var finalTotal = totalBytes > 0 ? totalBytes : written;
        var speed = ComputeSpeed(transferred, stopwatch.Elapsed);

        var actualHash = await ComputeSha256Async(partPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(partPath);
            throw new ModelChecksumMismatchException(expectedHash, actualHash);
        }

        // Same-volume move: the final path only appears once the content is verified.
        File.Move(partPath, destinationPath, overwrite: true);

        var completed = CreateProgress(written, finalTotal, speed);
        progress?.Report(completed);
        return completed;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, long offset, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        return await httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private Task DelayBeforeRetryAsync(CancellationToken cancellationToken) =>
        retryDelay > TimeSpan.Zero ? Task.Delay(retryDelay, cancellationToken) : Task.CompletedTask;

    private static bool IsRetryableStatus(HttpStatusCode? status) => status is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static string NormalizeHash(string hash)
    {
        var normalized = hash.Trim();
        const string prefix = "sha256:";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];
        return normalized.Trim().ToLowerInvariant();
    }

    private static void ValidateUri(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            throw new ArgumentException("The download URI must be absolute.", nameof(uri));
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The download URI must use HTTP or HTTPS.", nameof(uri));
    }

    private static long ComputeSpeed(long bytesTransferred, TimeSpan elapsed)
    {
        if (bytesTransferred <= 0) return 0;
        var seconds = elapsed.TotalSeconds;
        if (seconds <= 0) return bytesTransferred;
        var speed = bytesTransferred / seconds;
        return speed >= long.MaxValue ? long.MaxValue : (long)speed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // A locked file is handled by the next attempt (or surfaces as a download failure).
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only leftovers should not mask the original download error.
        }
    }
}
