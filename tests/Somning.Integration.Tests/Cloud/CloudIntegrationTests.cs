using System.Net;
using System.Net.Sockets;
using System.Text;
using Somning.App.Engine;
using Somning.Core.Cloud;
using Somning.Core.Engine;
using Somning.Core.Ports;
using Somning.Core.Settings;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Cloud;

/// <summary>
/// End-to-end cloud adapter tests against a local <see cref="HttpListener"/> mock server (STT-05, STT-06,
/// APP-05, APP-06). The router is exercised exactly as the app wires it, and the real WAV fixture is uploaded
/// so multipart packaging, key handling and error classification are covered together.
/// </summary>
public sealed class CloudIntegrationTests
{
    private const string GroqKey = "gsk_test_1234567890abcdef";
    private const string OpenAiKey = "sk-test-1234567890abcdef";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // ---- Happy paths ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Groq_happy_path_returns_the_text_and_cloud_backend()
    {
        await using var server = new MockCloudServer("""{"text":"hoje é sexta-feira"}""");
        using var http = new HttpClient();
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var router = Router(local, server.Endpoint, http, CloudProvider.Groq, GroqKey);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal("hoje é sexta-feira", result.Text);
        Assert.Equal("text", result.Outcome);
        Assert.Null(result.ErrorCode);
        Assert.False(result.Retryable);
        Assert.Equal("cloud-groq", result.Backend);
        Assert.Equal("whisper-large-v3-turbo", result.Model);
        Assert.True(result.InferMs >= 0);
        Assert.True(result.DurationMs > 0);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));

        Assert.Equal($"Bearer {GroqKey}", server.LastAuthorization);
        Assert.StartsWith("multipart/form-data", server.LastContentType ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("whisper-large-v3-turbo", server.LastBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("hoje.wav", server.LastBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("language", server.LastBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("response_format", server.LastBody ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("pt", server.LastBody ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAi_happy_path_returns_the_text_and_openai_backend()
    {
        await using var server = new MockCloudServer("""{"text":"hoje é sexta-feira"}""");
        using var http = new HttpClient();
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var router = Router(local, server.Endpoint, http, CloudProvider.OpenAi, OpenAiKey);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal("hoje é sexta-feira", result.Text);
        Assert.Equal("cloud-openai", result.Backend);
        Assert.Equal("whisper-1", result.Model);
        Assert.Equal(0, local.Calls);
        Assert.Contains("whisper-1", server.LastBody ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Custom_vocabulary_is_forwarded_as_the_prompt()
    {
        await using var server = new MockCloudServer("""{"text":"Somning"}""");
        using var http = new HttpClient();
        using var recording = CopyFixture();
        var router = Router(
            new LocalSpy(), server.Endpoint, http, CloudProvider.Groq, GroqKey, vocabulary: "Somning, Groq");

        await router.TranscribeAsync(recording.Path, Token);

        Assert.Contains("Somning, Groq", server.LastBody ?? string.Empty, StringComparison.Ordinal);
    }

    // ---- Failures keep the recording and never fall back -----------------------------------------------

    [Fact]
    public async Task Unauthorized_maps_to_invalid_api_key_and_preserves_the_recording()
    {
        await using var server = new MockCloudServer("""{"error":{"message":"bad key"}}""", HttpStatusCode.Unauthorized);
        using var http = new HttpClient();
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var router = Router(local, server.Endpoint, http, CloudProvider.Groq, GroqKey);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal("error", result.Outcome);
        Assert.Equal(CloudErrorCodes.InvalidApiKey, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal("cloud-groq", result.Backend);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));
        Assert.DoesNotContain(GroqKey, result.Reason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limit_maps_to_rate_limit_and_preserves_the_recording()
    {
        await using var server = new MockCloudServer("""{"error":{"message":"slow down"}}""", HttpStatusCode.TooManyRequests);
        using var http = new HttpClient();
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var router = Router(local, server.Endpoint, http, CloudProvider.OpenAi, OpenAiKey);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal(CloudErrorCodes.RateLimit, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal("cloud-openai", result.Backend);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));
    }

    [Fact]
    public async Task Timeout_maps_to_timeout_and_preserves_the_recording()
    {
        await using var server = new MockCloudServer("""{"text":"too late"}""") { Delay = TimeSpan.FromSeconds(5) };
        using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var router = Router(local, server.Endpoint, http, CloudProvider.Groq, GroqKey);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal(CloudErrorCodes.Timeout, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));
    }

    [Fact]
    public async Task Connection_refused_maps_to_no_network_and_preserves_the_recording()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var port = ReservePort();
        var endpoint = new Uri($"http://127.0.0.1:{port}/v1/audio/transcriptions");
        var router = Router(local, endpoint, http, CloudProvider.Groq, GroqKey);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal(CloudErrorCodes.NoNetwork, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));
    }

    [Fact]
    public async Task A_missing_key_fails_fast_with_invalid_api_key_and_no_request()
    {
        await using var server = new MockCloudServer("""{"text":"never"}""");
        using var http = new HttpClient();
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var settings = new UserSettings { CloudProvider = CloudProvider.Groq };
        var router = new RouterTranscriber(
            local,
            new CloudTranscriptionClient(http, server.Endpoint),
            () => settings);

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal(CloudErrorCodes.InvalidApiKey, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal(0, local.Calls);
        Assert.Equal(0, server.Requests);
        Assert.True(File.Exists(recording.Path));
    }

    [Fact]
    public async Task Switching_back_to_local_routes_to_whisper_again()
    {
        await using var server = new MockCloudServer("""{"text":"cloud"}""");
        using var http = new HttpClient();
        var local = new LocalSpy();
        using var recording = CopyFixture();
        var settings = new UserSettings { CloudProvider = CloudProvider.Groq, EncryptedApiKey = GroqKey };
        var router = new RouterTranscriber(
            local,
            new CloudTranscriptionClient(http, server.Endpoint),
            () => settings);

        var cloud = await router.TranscribeAsync(recording.Path, Token);
        settings = settings with { CloudProvider = CloudProvider.None };
        var localResult = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal("cloud", cloud.Text);
        Assert.Equal("local transcript", localResult.Text);
        Assert.Equal(1, local.Calls);
    }

    // ---- Real providers (opt-in, skipped without keys) -------------------------------------------------

    [Fact]
    public async Task Real_groq_transcription_when_the_key_is_available()
    {
        var key = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        Requires.That(!string.IsNullOrWhiteSpace(key), "GROQ_API_KEY is not set.");

        using var recording = CopyFixture();
        var router = new RouterTranscriber(
            new LocalSpy(),
            new CloudTranscriptionClient(),
            () => new UserSettings { CloudProvider = CloudProvider.Groq, EncryptedApiKey = key });

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Null(result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.Equal("cloud-groq", result.Backend);
    }

    [Fact]
    public async Task Real_openai_transcription_when_the_key_is_available()
    {
        var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        Requires.That(!string.IsNullOrWhiteSpace(key), "OPENAI_API_KEY is not set.");

        using var recording = CopyFixture();
        var router = new RouterTranscriber(
            new LocalSpy(),
            new CloudTranscriptionClient(),
            () => new UserSettings { CloudProvider = CloudProvider.OpenAi, EncryptedApiKey = key });

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Null(result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Text));
        Assert.Equal("cloud-openai", result.Backend);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    private static RouterTranscriber Router(
        LocalSpy local,
        Uri endpoint,
        HttpClient http,
        string provider,
        string key,
        string vocabulary = "") =>
        new(
            local,
            new CloudTranscriptionClient(http, endpoint),
            () => new UserSettings
            {
                CloudProvider = provider,
                EncryptedApiKey = key,
                CustomVocabulary = vocabulary
            });

    private static TempRecording CopyFixture()
    {
        var source = Path.Combine(RepoPaths.Fixtures, "tracer", "hoje.wav");
        var directory = Path.Combine(Path.GetTempPath(), "somning-cloud-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "hoje.wav");
        File.Copy(source, destination);
        return new TempRecording(directory, destination);
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class LocalSpy : ITranscriber
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);
        public EngineStatus Status => EngineStatus.Ready;
        public string? ActiveBackend => "cpu";
        public event Action<EngineStatus>? StatusChanged { add { } remove { } }

        public Task StartAsync(string backend, CancellationToken ct) => Task.CompletedTask;

        public Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new EngineTranscription(
                "local transcript", "text", null, null, false, 0, 0, 0, 0, 0, 0, "cpu", "local-model", 0));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TempRecording(string directory, string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>Minimal OpenAI-compatible transcription endpoint backed by <see cref="HttpListener"/>.</summary>
    private sealed class MockCloudServer : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Task acceptLoop;
        private readonly string responseBody;
        private readonly HttpStatusCode status;
        private int requests;
        private bool disposed;

        public MockCloudServer(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
        {
            this.responseBody = responseBody;
            this.status = status;

            var port = ReservePort();
            Endpoint = new Uri($"http://127.0.0.1:{port}/v1/audio/transcriptions");
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public Uri Endpoint { get; }
        public TimeSpan Delay { get; init; }
        public int Requests => Volatile.Read(ref requests);
        public string? LastAuthorization { get; private set; }
        public string? LastContentType { get; private set; }
        public string? LastBody { get; private set; }

        private async Task AcceptLoopAsync()
        {
            while (!disposed)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            Interlocked.Increment(ref requests);
            LastAuthorization = context.Request.Headers["Authorization"];
            LastContentType = context.Request.ContentType;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                LastBody = await reader.ReadToEndAsync().ConfigureAwait(false);

            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay).ConfigureAwait(false);

            var payload = Encoding.UTF8.GetBytes(responseBody);
            try
            {
                context.Response.StatusCode = (int)status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = payload.Length;
                await context.Response.OutputStream.WriteAsync(payload).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException)
            {
                // The client timed out or disconnected; nothing left to answer.
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            try { listener.Stop(); }
            catch (ObjectDisposedException) { }
            listener.Close();
            try
            {
                await acceptLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or HttpListenerException)
            {
                // The listener is already shutting down.
            }
        }
    }
}
