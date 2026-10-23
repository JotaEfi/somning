using System.Net;
using System.Security.Cryptography;
using Somning.Core.Model;
using Xunit;

namespace Somning.Integration.Tests.Model;

public sealed class ModelDownloadIntegrationTests : IDisposable
{
    private readonly HttpListener listener;
    private readonly string baseUrl;
    private readonly string tempDir;
    private readonly byte[] testPayload;
    private readonly string expectedSha256;

    public ModelDownloadIntegrationTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "somning-test-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Generate 64 KB test payload
        testPayload = new byte[65_536];
        new Random(42).NextBytes(testPayload);
        expectedSha256 = Convert.ToHexString(SHA256.HashData(testPayload)).ToLowerInvariant();

        // Start local HttpListener on dynamic port
        listener = new HttpListener();
        var port = GetAvailablePort();
        baseUrl = $"http://127.0.0.1:{port}/";
        listener.Prefixes.Add(baseUrl);
        listener.Start();
    }

    [Fact]
    public async Task FullDownload_Succeeds_AndVerifiesSha256()
    {
        var ct = TestContext.Current.CancellationToken;
        var destination = Path.Combine(tempDir, "model.bin");

        _ = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = testPayload.Length;
            await context.Response.OutputStream.WriteAsync(testPayload, ct);
            context.Response.OutputStream.Close();
        }, ct);

        var downloader = new ModelDownloadManager();
        var progressList = new List<ModelDownloadProgress>();
        var progress = new Progress<ModelDownloadProgress>(p => progressList.Add(p));

        var result = await downloader.DownloadAsync(
            new Uri(baseUrl + "model.bin"),
            destination,
            expectedSha256,
            progress,
            ct);

        Assert.True(File.Exists(destination));
        Assert.Equal(testPayload.Length, new FileInfo(destination).Length);
        Assert.Equal(100.0, result.Percent);

        var downloadedBytes = await File.ReadAllBytesAsync(destination, ct);
        Assert.Equal(testPayload, downloadedBytes);
    }

    [Fact]
    public async Task ResumedDownload_UsesRangeHeader_AndSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var destination = Path.Combine(tempDir, "resumed-model.bin");
        var partPath = destination + ".part";

        // Pre-create partial file with first 32 KB
        int partialSize = 32_768;
        await File.WriteAllBytesAsync(partPath, testPayload[..partialSize], ct);

        string? receivedRange = null;

        _ = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            receivedRange = context.Request.Headers["Range"];

            if (receivedRange == $"bytes={partialSize}-")
            {
                var remaining = testPayload[partialSize..];
                context.Response.StatusCode = 206; // Partial Content
                context.Response.AddHeader("Content-Range", $"bytes {partialSize}-{testPayload.Length - 1}/{testPayload.Length}");
                context.Response.ContentLength64 = remaining.Length;
                await context.Response.OutputStream.WriteAsync(remaining, ct);
            }
            else
            {
                context.Response.StatusCode = 200;
                context.Response.ContentLength64 = testPayload.Length;
                await context.Response.OutputStream.WriteAsync(testPayload, ct);
            }
            context.Response.OutputStream.Close();
        }, ct);

        var downloader = new ModelDownloadManager();
        var result = await downloader.DownloadAsync(
            new Uri(baseUrl + "resumed-model.bin"),
            destination,
            expectedSha256,
            cancellationToken: ct);

        Assert.NotNull(receivedRange);
        Assert.Equal($"bytes={partialSize}-", receivedRange);
        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(partPath)); // .part moved to destination
        Assert.Equal(testPayload.Length, new FileInfo(destination).Length);

        var downloadedBytes = await File.ReadAllBytesAsync(destination, ct);
        Assert.Equal(testPayload, downloadedBytes);
    }

    [Fact]
    public async Task CorruptedDownload_ThrowsChecksumMismatch_AndDeletesCorruptedFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var destination = Path.Combine(tempDir, "corrupted-model.bin");
        var partPath = destination + ".part";
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

        _ = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            context.Response.StatusCode = 200;
            context.Response.ContentLength64 = testPayload.Length;
            await context.Response.OutputStream.WriteAsync(testPayload, ct);
            context.Response.OutputStream.Close();
        }, ct);

        var downloader = new ModelDownloadManager();
        await Assert.ThrowsAsync<ModelChecksumMismatchException>(async () =>
        {
            await downloader.DownloadAsync(
                new Uri(baseUrl + "corrupted-model.bin"),
                destination,
                wrongHash,
                cancellationToken: ct);
        });

        // Corrupted file and partial file must be deleted (STT-07)
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(partPath));
    }

    [Fact]
    public async Task RetryableServerFailure_503_RetriesAndSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var destination = Path.Combine(tempDir, "retry-model.bin");

        int requestCount = 0;
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    requestCount++;
                    if (requestCount == 1)
                    {
                        // First attempt: 503 Service Unavailable (retryable)
                        context.Response.StatusCode = 503;
                        context.Response.Close();
                    }
                    else
                    {
                        // Second attempt: 200 OK
                        context.Response.StatusCode = 200;
                        context.Response.ContentLength64 = testPayload.Length;
                        await context.Response.OutputStream.WriteAsync(testPayload, ct);
                        context.Response.OutputStream.Close();
                        break;
                    }
                }
                catch { break; }
            }
        }, ct);

        var downloader = new ModelDownloadManager(maxAttempts: 3, retryDelay: TimeSpan.FromMilliseconds(50));
        var result = await downloader.DownloadAsync(
            new Uri(baseUrl + "retry-model.bin"),
            destination,
            expectedSha256,
            cancellationToken: ct);

        Assert.Equal(2, requestCount);
        Assert.True(File.Exists(destination));
        Assert.Equal(testPayload.Length, new FileInfo(destination).Length);
    }

    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        try { listener.Stop(); } catch { }
        try { listener.Close(); } catch { }
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch { }
    }
}
