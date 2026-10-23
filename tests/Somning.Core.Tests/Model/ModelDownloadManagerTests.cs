using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Somning.Core.Model;
using Xunit;

namespace Somning.Core.Tests.Model;

public sealed class ModelDownloadManagerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- SHA-256 ------------------------------------------------------------------------------------------

    [Fact]
    public void Sha256_of_bytes_matches_the_known_vectors()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ModelDownloadManager.ComputeSha256(ReadOnlySpan<byte>.Empty));
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ModelDownloadManager.ComputeSha256("abc"u8));
    }

    [Fact]
    public async Task Sha256_of_a_file_matches_the_in_memory_hash()
    {
        using var root = new TempDir();
        var path = Path.Combine(root.Path, "sample.bin");
        var payload = Encoding.UTF8.GetBytes("model payload for hashing");
        await File.WriteAllBytesAsync(path, payload, Token);

        var fileHash = await ModelDownloadManager.ComputeSha256Async(path, Token);

        Assert.Equal(ModelDownloadManager.ComputeSha256(payload), fileHash);
    }

    // ---- Progress -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 100, 0d)]
    [InlineData(50, 200, 25d)]
    [InlineData(200, 200, 100d)]
    [InlineData(300, 200, 100d)]
    [InlineData(10, 0, 0d)]
    [InlineData(-5, 100, 0d)]
    public void Percent_is_clamped_between_zero_and_one_hundred(long downloaded, long total, double expected)
    {
        Assert.Equal(expected, ModelDownloadManager.ComputePercent(downloaded, total), precision: 6);
    }

    [Fact]
    public void Create_progress_derives_the_percentage_and_keeps_the_counters()
    {
        var progress = ModelDownloadManager.CreateProgress(512, 2_048, 4_096);

        Assert.Equal(512, progress.BytesDownloaded);
        Assert.Equal(2_048, progress.TotalBytes);
        Assert.Equal(25d, progress.Percent, precision: 6);
        Assert.Equal(4_096, progress.SpeedBps);
    }

    [Fact]
    public void Create_progress_sanitizes_negative_values()
    {
        var progress = ModelDownloadManager.CreateProgress(-1, -2, -3);

        Assert.Equal(0, progress.BytesDownloaded);
        Assert.Equal(0, progress.TotalBytes);
        Assert.Equal(0d, progress.Percent);
        Assert.Equal(0, progress.SpeedBps);
    }

    [Fact]
    public void Partial_path_appends_the_part_suffix()
    {
        Assert.Equal("model.bin.part", ModelDownloadManager.GetPartialPath("model.bin"));
        Assert.Throws<ArgumentException>(() => ModelDownloadManager.GetPartialPath("  "));
    }

    // ---- Retry classification -----------------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public void Http_status_codes_are_classified(HttpStatusCode status, bool expected)
    {
        var exception = new HttpRequestException("failure", inner: null, status);

        Assert.Equal(expected, ModelDownloadManager.IsRetryable(exception));
    }

    [Fact]
    public void Transient_transport_exceptions_are_retryable()
    {
        Assert.True(ModelDownloadManager.IsRetryable(new TimeoutException()));
        Assert.True(ModelDownloadManager.IsRetryable(new SocketException((int)SocketError.ConnectionReset)));
        Assert.True(ModelDownloadManager.IsRetryable(new IOException("connection dropped")));
        Assert.True(ModelDownloadManager.IsRetryable(new TaskCanceledException("timeout", new TimeoutException())));
    }

    [Fact]
    public void Permanent_and_cancellation_exceptions_are_not_retryable()
    {
        Assert.False(ModelDownloadManager.IsRetryable(new OperationCanceledException()));
        Assert.False(ModelDownloadManager.IsRetryable(new InvalidOperationException()));
        Assert.False(ModelDownloadManager.IsRetryable(null));
        Assert.False(ModelDownloadManager.IsRetryable(new ModelChecksumMismatchException("a", "b")));
    }

    [Fact]
    public void Wrapped_failures_inherit_the_classification()
    {
        var serviceUnavailable = new HttpRequestException("503", inner: null, HttpStatusCode.ServiceUnavailable);
        var retryable = new ModelDownloadFailedException("wrapped", isRetryable: false, serviceUnavailable);

        Assert.True(ModelDownloadManager.IsRetryable(retryable));
        Assert.False(ModelDownloadManager.IsRetryable(new ModelDownloadFailedException("auth", isRetryable: false)));
        Assert.True(ModelDownloadManager.IsRetryable(new ModelDownloadFailedException("network", isRetryable: true)));
        Assert.True(ModelDownloadManager.IsRetryable(new AggregateException(serviceUnavailable)));
    }

    // ---- Download -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Successful_download_verifies_the_hash_reports_progress_and_removes_the_part_file()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("downloaded model content");
        var handler = new StubHandler((_, _) => Ok(payload));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);
        var destination = Path.Combine(root.Path, "model.bin");
        var progress = new ProgressCollector();

        var result = await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress,
            Token);

        Assert.Equal(1, handler.Calls);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(ModelDownloadManager.GetPartialPath(destination)));
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination, Token));
        Assert.Equal(payload.Length, result.BytesDownloaded);
        Assert.Equal(payload.Length, result.TotalBytes);
        Assert.Equal(100d, result.Percent, precision: 6);
        Assert.Equal(100d, progress.Values[^1].Percent, precision: 6);
    }

    [Fact]
    public async Task Existing_valid_destination_is_not_downloaded_again()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("already here");
        var destination = Path.Combine(root.Path, "model.bin");
        await File.WriteAllBytesAsync(destination, payload, Token);
        var handler = new StubHandler((_, _) => Ok(Encoding.UTF8.GetBytes("new content")));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);

        var result = await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress: null,
            Token);

        Assert.Equal(0, handler.Calls);
        Assert.Equal(100d, result.Percent, precision: 6);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination, Token));
    }

    [Fact]
    public async Task Partial_file_is_resumed_with_a_range_request()
    {
        using var root = new TempDir();
        var payload = Enumerable.Range(0, 16_384).Select(index => (byte)(index % 251)).ToArray();
        var half = payload.Length / 2;
        var destination = Path.Combine(root.Path, "model.bin");
        var partPath = ModelDownloadManager.GetPartialPath(destination);
        await File.WriteAllBytesAsync(partPath, payload[..half], Token);

        var handler = new StubHandler((request, _) =>
        {
            var range = Assert.Single(request.Headers.Range!.Ranges);
            Assert.Equal(half, range.From!.Value);
            Assert.Null(range.To);
            return Partial(payload[half..], half, payload.Length);
        });
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);

        var result = await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress: null,
            Token);

        Assert.Equal(payload, await File.ReadAllBytesAsync(destination, Token));
        Assert.Equal(payload.Length, result.BytesDownloaded);
        Assert.Equal(100d, result.Percent, precision: 6);
        Assert.False(File.Exists(partPath));
    }

    [Fact]
    public async Task Server_ignoring_range_restarts_from_zero_instead_of_duplicating_bytes()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("full model content");
        var destination = Path.Combine(root.Path, "model.bin");
        var partPath = ModelDownloadManager.GetPartialPath(destination);
        await File.WriteAllBytesAsync(partPath, payload[..5], Token);

        // A 200 response means the server ignored Range: the manager must truncate the partial file.
        var handler = new StubHandler((_, _) => Ok(payload));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);

        await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress: null,
            Token);

        Assert.Equal(payload, await File.ReadAllBytesAsync(destination, Token));
    }

    [Fact]
    public async Task Unsatisifiable_range_retries_once_without_a_range_header()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("fresh download");
        var destination = Path.Combine(root.Path, "model.bin");
        var partPath = ModelDownloadManager.GetPartialPath(destination);
        await File.WriteAllBytesAsync(partPath, payload[..4], Token);

        var handler = new StubHandler((request, call) =>
        {
            if (call == 1)
            {
                Assert.NotNull(request.Headers.Range);
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            }

            Assert.Null(request.Headers.Range);
            return Ok(payload);
        });
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);

        await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress: null,
            Token);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination, Token));
    }

    [Fact]
    public async Task Checksum_mismatch_deletes_the_corrupt_file_and_never_creates_the_destination()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("corrupt bytes");
        var expected = ModelDownloadManager.ComputeSha256(Encoding.UTF8.GetBytes("expected bytes"));
        var handler = new StubHandler((_, _) => Ok(payload));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);
        var destination = Path.Combine(root.Path, "model.bin");
        var partPath = ModelDownloadManager.GetPartialPath(destination);

        var exception = await Assert.ThrowsAsync<ModelChecksumMismatchException>(() => manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, expected),
            progress: null,
            Token));

        Assert.Equal(expected, exception.Expected);
        Assert.Equal(ModelDownloadManager.ComputeSha256(payload), exception.Actual);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(partPath));
    }

    [Fact]
    public async Task Service_unavailable_is_retried_and_the_next_attempt_succeeds()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("retry payload");
        var handler = new StubHandler((_, call) => call == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Ok(payload));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 3, retryDelay: TimeSpan.Zero);
        var destination = Path.Combine(root.Path, "model.bin");

        await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress: null,
            Token);

        Assert.Equal(2, handler.Calls);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination, Token));
    }

    [Fact]
    public async Task Not_found_is_not_retried_and_surfaces_as_non_retryable()
    {
        using var root = new TempDir();
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 3, retryDelay: TimeSpan.Zero);
        var destination = Path.Combine(root.Path, "model.bin");

        var exception = await Assert.ThrowsAsync<ModelDownloadFailedException>(() => manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, "abc"),
            progress: null,
            Token));

        Assert.False(exception.IsRetryable);
        Assert.Equal(1, handler.Calls);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Too_many_retryable_failures_throw_the_last_failure()
    {
        using var root = new TempDir();
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 2, retryDelay: TimeSpan.Zero);
        var destination = Path.Combine(root.Path, "model.bin");

        var exception = await Assert.ThrowsAsync<ModelDownloadFailedException>(() => manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, "abc"),
            progress: null,
            Token));

        Assert.True(exception.IsRetryable);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Destination_directory_is_created_on_demand()
    {
        using var root = new TempDir();
        var payload = Encoding.UTF8.GetBytes("nested");
        var destination = Path.Combine(root.Path, "models", "nested", "model.bin");
        var handler = new StubHandler((_, _) => Ok(payload));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);

        await manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, ModelDownloadManager.ComputeSha256(payload)),
            progress: null,
            Token);

        Assert.True(File.Exists(destination));
    }

    [Fact]
    public async Task Invalid_request_is_rejected_before_any_network_call()
    {
        using var root = new TempDir();
        var handler = new StubHandler((_, _) => Ok([]));
        using var client = new HttpClient(handler);
        var manager = new ModelDownloadManager(client, maxAttempts: 1);
        var destination = Path.Combine(root.Path, "model.bin");

        await Assert.ThrowsAsync<ArgumentException>(() => manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("file:///model.bin"), destination, "abc"),
            progress: null,
            Token));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), "  ", "abc"),
            progress: null,
            Token));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.DownloadAsync(
            new ModelDownloadRequest(new Uri("http://localhost/model.bin"), destination, "  "),
            progress: null,
            Token));

        Assert.Equal(0, handler.Calls);
    }

    // ---- Helpers ------------------------------------------------------------------------------------------

    private static HttpResponseMessage Ok(byte[] payload)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentLength = payload.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Partial(byte[] remainder, long from, long total)
    {
        var content = new ByteArrayContent(remainder);
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, total - 1, total);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request, Interlocked.Increment(ref calls)));
        }
    }

    private sealed class ProgressCollector : IProgress<ModelDownloadProgress>
    {
        private readonly List<ModelDownloadProgress> values = [];

        public IReadOnlyList<ModelDownloadProgress> Values
        {
            get
            {
                lock (values) return [.. values];
            }
        }

        public void Report(ModelDownloadProgress value)
        {
            lock (values) values.Add(value);
        }
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "somning-model-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS removes the temp directory eventually.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
