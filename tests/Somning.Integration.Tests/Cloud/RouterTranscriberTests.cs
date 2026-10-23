using Somning.App.Engine;
using Somning.Core.Cloud;
using Somning.Core.Engine;
using Somning.Core.Ports;
using Somning.Core.Settings;
using Xunit;

namespace Somning.Integration.Tests.Cloud;

public sealed class RouterTranscriberTests
{
    private const string PlainKey = "gsk_live_1234567890abcdef";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Local_provider_delegates_to_the_local_engine()
    {
        var local = new LocalSpy();
        var cloud = new CloudSpy();
        var router = new RouterTranscriber(local, cloud, () => Settings(CloudProvider.None));

        var result = await router.TranscribeAsync("capture.wav", Token);

        Assert.Equal("local transcript", result.Text);
        Assert.Equal("cpu", result.Backend);
        Assert.Equal(1, local.Calls);
        Assert.Equal(0, cloud.Calls);
    }

    [Fact]
    public async Task Groq_route_decrypts_the_key_and_calls_the_cloud_client()
    {
        var local = new LocalSpy();
        var cloud = new CloudSpy
        {
            Result = new CloudTranscriptionResult("hoje é sexta-feira", 1_500, 320, "whisper-large-v3-turbo")
        };
        var router = new RouterTranscriber(
            local,
            cloud,
            () => Settings(CloudProvider.Groq, "enc:" + PlainKey, "Somning, Groq"),
            new PrefixProtector());

        var result = await router.TranscribeAsync("capture.wav", Token);

        Assert.Equal(1, cloud.Calls);
        Assert.Equal(0, local.Calls);
        var config = Assert.IsType<CloudEngineConfig>(cloud.LastConfig);
        Assert.Equal(CloudProvider.Groq, config.Provider);
        Assert.Equal(PlainKey, config.ApiKey);
        Assert.Equal("Somning, Groq", config.CustomVocabulary);
        Assert.DoesNotContain(PlainKey, config.ToString(), StringComparison.Ordinal);

        Assert.Equal("hoje é sexta-feira", result.Text);
        Assert.Equal("text", result.Outcome);
        Assert.Null(result.ErrorCode);
        Assert.False(result.Retryable);
        Assert.Equal("cloud-groq", result.Backend);
        Assert.Equal("whisper-large-v3-turbo", result.Model);
        Assert.Equal(1_500, result.DurationMs);
        Assert.Equal(320, result.InferMs);
    }

    [Fact]
    public async Task OpenAi_route_uses_the_openai_backend_and_model()
    {
        var local = new LocalSpy();
        var cloud = new CloudSpy
        {
            Result = new CloudTranscriptionResult("bom dia", 900, 180, "whisper-1")
        };
        var router = new RouterTranscriber(
            local,
            cloud,
            () => Settings("  OPENAI ", "enc:sk-1234567890abcdef"),
            new PrefixProtector());

        var result = await router.TranscribeAsync("capture.wav", Token);

        Assert.Equal(CloudProvider.OpenAi, cloud.LastConfig!.Provider);
        Assert.Equal("cloud-openai", result.Backend);
        Assert.Equal("whisper-1", result.Model);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Empty_cloud_text_is_reported_as_empty()
    {
        var cloud = new CloudSpy { Result = new CloudTranscriptionResult("   ", 800, 90, "whisper-1") };
        var router = new RouterTranscriber(new LocalSpy(), cloud, () => Settings(CloudProvider.OpenAi, PlainKey));

        var result = await router.TranscribeAsync("capture.wav", Token);

        Assert.Equal("empty", result.Outcome);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Missing_key_reports_invalid_api_key_without_calling_any_engine()
    {
        var local = new LocalSpy();
        var cloud = new CloudSpy();
        var router = new RouterTranscriber(local, cloud, () => Settings(CloudProvider.Groq), new PrefixProtector());

        var result = await router.TranscribeAsync("capture.wav", Token);

        Assert.Equal("error", result.Outcome);
        Assert.Equal(CloudErrorCodes.InvalidApiKey, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal("cloud-groq", result.Backend);
        Assert.Equal("whisper-large-v3-turbo", result.Model);
        Assert.Equal(0, cloud.Calls);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task An_unreadable_key_reports_invalid_api_key_instead_of_crashing()
    {
        var cloud = new CloudSpy();
        var router = new RouterTranscriber(
            new LocalSpy(),
            cloud,
            () => Settings(CloudProvider.Groq, "corrupt-ciphertext"),
            new ThrowingProtector());

        var result = await router.TranscribeAsync("capture.wav", Token);

        Assert.Equal(CloudErrorCodes.InvalidApiKey, result.ErrorCode);
        Assert.Equal(0, cloud.Calls);
    }

    [Theory]
    [InlineData(CloudErrorCodes.InvalidApiKey, 401)]
    [InlineData(CloudErrorCodes.RateLimit, 429)]
    [InlineData(CloudErrorCodes.Timeout, 408)]
    [InlineData(CloudErrorCodes.NoNetwork, null)]
    public async Task Cloud_failures_keep_the_recording_and_never_fall_back_to_local(
        string errorCode,
        int? status)
    {
        var local = new LocalSpy();
        var cloud = new CloudSpy
        {
            Failure = _ => new CloudTranscriptionException(
                errorCode,
                $"provider rejected key {PlainKey}",
                status)
        };
        var router = new RouterTranscriber(
            local,
            cloud,
            () => Settings(CloudProvider.Groq, "enc:" + PlainKey),
            new PrefixProtector());
        using var recording = new TempRecording();

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal("error", result.Outcome);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal("cloud-groq", result.Backend);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));
        Assert.DoesNotContain(PlainKey, result.Reason ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("gsk***def", result.Reason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unexpected_cloud_exception_maps_to_cloud_error_and_keeps_the_recording()
    {
        var local = new LocalSpy();
        var cloud = new CloudSpy { Failure = _ => new InvalidOperationException("socket disposed") };
        var router = new RouterTranscriber(local, cloud, () => Settings(CloudProvider.Groq, PlainKey));
        using var recording = new TempRecording();

        var result = await router.TranscribeAsync(recording.Path, Token);

        Assert.Equal("cloud_error", result.ErrorCode);
        Assert.True(result.Retryable);
        Assert.Equal(0, local.Calls);
        Assert.True(File.Exists(recording.Path));
    }

    [Fact]
    public async Task Cancellation_propagates_to_the_caller()
    {
        var cloud = new CloudSpy
        {
            Failure = ct => throw new OperationCanceledException(ct)
        };
        var router = new RouterTranscriber(new LocalSpy(), cloud, () => Settings(CloudProvider.Groq, PlainKey));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => router.TranscribeAsync("capture.wav", cancellation.Token));
    }

    [Fact]
    public async Task Status_events_start_and_dispose_delegate_to_the_local_engine()
    {
        var local = new LocalSpy { Status = EngineStatus.Restarting };
        var router = new RouterTranscriber(local, new CloudSpy(), () => Settings(CloudProvider.None));

        Assert.Equal(EngineStatus.Restarting, router.Status);
        Assert.Equal("cpu", router.ActiveBackend);

        var seen = new List<EngineStatus>();
        router.StatusChanged += seen.Add;
        local.RaiseStatus(EngineStatus.Ready);
        Assert.Equal([EngineStatus.Ready], seen);

        await router.StartAsync("vulkan", Token);
        Assert.Equal("vulkan", local.StartedBackend);
        Assert.Equal(EngineStatus.Ready, router.Status);

        await router.DisposeAsync();
        Assert.True(local.Disposed);
    }

    [Fact]
    public void Active_backend_reflects_the_configured_provider()
    {
        var settings = Settings(CloudProvider.None);
        var router = new RouterTranscriber(new LocalSpy(), new CloudSpy(), () => settings);

        Assert.Equal("cpu", router.ActiveBackend);

        settings = Settings(CloudProvider.Groq);
        Assert.Equal("cloud-groq", router.ActiveBackend);

        settings = Settings(CloudProvider.OpenAi);
        Assert.Equal("cloud-openai", router.ActiveBackend);
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    private static UserSettings Settings(string provider, string? encryptedKey = null, string vocabulary = "") =>
        new()
        {
            CloudProvider = provider,
            EncryptedApiKey = encryptedKey,
            CustomVocabulary = vocabulary
        };

    private sealed class LocalSpy : ITranscriber
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);
        public EngineStatus Status { get; set; } = EngineStatus.Ready;
        public string? ActiveBackend => "cpu";
        public string? StartedBackend { get; private set; }
        public bool Disposed { get; private set; }

        public event Action<EngineStatus>? StatusChanged;

        public void RaiseStatus(EngineStatus status)
        {
            Status = status;
            StatusChanged?.Invoke(status);
        }

        public Task StartAsync(string backend, CancellationToken ct)
        {
            StartedBackend = backend;
            return Task.CompletedTask;
        }

        public Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new EngineTranscription(
                "local transcript", "text", null, null, false, 0, 0, 0, 0, 0, 0, "cpu", "local-model", 0));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CloudSpy : ICloudTranscriptionClient
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);
        public CloudEngineConfig? LastConfig { get; private set; }
        public CloudTranscriptionResult Result { get; set; } = new("cloud transcript", 1_000, 100, "whisper-1");
        public Func<CancellationToken, Exception>? Failure { get; set; }

        public Task<CloudTranscriptionResult> TranscribeAsync(
            string wavPath,
            CloudEngineConfig config,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref calls);
            LastConfig = config;
            return Failure is { } failure
                ? Task.FromException<CloudTranscriptionResult>(failure(cancellationToken))
                : Task.FromResult(Result);
        }
    }

    private sealed class PrefixProtector : ISettingsProtector
    {
        private const string Prefix = "enc:";

        public string Protect(string plainText) => Prefix + plainText;

        public string Unprotect(string cipherText) =>
            cipherText.StartsWith(Prefix, StringComparison.Ordinal)
                ? cipherText[Prefix.Length..]
                : string.Empty;
    }

    private sealed class ThrowingProtector : ISettingsProtector
    {
        public string Protect(string plainText) => plainText;

        public string Unprotect(string cipherText) => throw new InvalidOperationException("wrong user");
    }

    private sealed class TempRecording : IDisposable
    {
        public TempRecording()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "somning-router-tests",
                Guid.NewGuid().ToString("N") + ".wav");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllBytes(Path, new byte[128]);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path)) File.Delete(Path);
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
}
