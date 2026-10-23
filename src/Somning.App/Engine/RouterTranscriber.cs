using Serilog;
using Somning.Core.Cloud;
using Somning.Core.Engine;
using Somning.Core.Ports;
using Somning.Core.Settings;

namespace Somning.App.Engine;

/// <summary>
/// Routes each dictation to the configured engine (STT-05): <c>none</c> keeps the local Whisper supervisor,
/// <c>groq</c>/<c>openai</c> go to the cloud client after decrypting the API key from settings. Cloud failures
/// are returned as retryable errors with an explicit code and never fall back to local Whisper (STT-06); the
/// recording is left untouched so the user can retry it (APP-05). The key is never logged or embedded in the
/// returned reason (APP-06).
/// </summary>
public sealed class RouterTranscriber : ITranscriber
{
    private readonly ITranscriber local;
    private readonly ICloudTranscriptionClient cloud;
    private readonly Func<UserSettings> getSettings;
    private readonly ISettingsProtector? protector;

    public RouterTranscriber(
        ITranscriber localTranscriber,
        ICloudTranscriptionClient? cloudClient = null,
        Func<UserSettings>? getSettings = null,
        ISettingsProtector? protector = null)
    {
        ArgumentNullException.ThrowIfNull(localTranscriber);

        local = localTranscriber;
        cloud = cloudClient ?? new CloudTranscriptionClient();
        this.getSettings = getSettings ?? (static () => new UserSettings());
        this.protector = protector;
    }

    public EngineStatus Status => local.Status;

    public event Action<EngineStatus>? StatusChanged
    {
        add => local.StatusChanged += value;
        remove => local.StatusChanged -= value;
    }

    /// <summary>Backend of the engine that will serve the next dictation.</summary>
    public string? ActiveBackend
    {
        get
        {
            var provider = CloudProvider.Normalize(getSettings().CloudProvider);
            return CloudProvider.IsCloud(provider) ? BackendFor(provider) : local.ActiveBackend;
        }
    }

    /// <summary>The local engine always starts as required by the app lifecycle; the route is decided per request.</summary>
    public Task StartAsync(string backend, CancellationToken ct) => local.StartAsync(backend, ct);

    public async Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct)
    {
        var settings = getSettings();
        var provider = CloudProvider.Normalize(settings.CloudProvider);
        if (!CloudProvider.IsCloud(provider))
            return await local.TranscribeAsync(wavPath, ct).ConfigureAwait(false);

        var backend = BackendFor(provider);
        var model = CloudProvider.DefaultModel(provider);
        var apiKey = ResolveApiKey(settings);
        if (apiKey.Length == 0)
            return Failure(CloudErrorCodes.InvalidApiKey, "Cloud API key is missing or unreadable.", backend, model);

        try
        {
            var config = new CloudEngineConfig(provider, apiKey, settings.CustomVocabulary);
            var result = await cloud.TranscribeAsync(wavPath, config, ct).ConfigureAwait(false);
            var outcome = string.IsNullOrWhiteSpace(result.Text) ? "empty" : "text";
            return new EngineTranscription(result.Text, outcome, null, null, false, result.DurationMs,
                0, result.InferMs, 0, 0, 0, backend, result.Model, 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudTranscriptionException ex)
        {
            // Retryable even for invalid_api_key: the WAV stays on disk so the user can fix the key and retry.
            return Failure(ex.ErrorCode, ApiKeyMasker.Redact(ex.Message, apiKey), backend, model);
        }
        catch (Exception ex)
        {
            Log.Warning("Cloud transcription failed unexpectedly for provider {Provider} ({ErrorType})",
                provider, ex.GetType().Name);
            return Failure("cloud_error", ApiKeyMasker.Redact(ex.Message, apiKey), backend, model);
        }
    }

    private string ResolveApiKey(UserSettings settings)
    {
        var stored = settings.EncryptedApiKey;
        if (string.IsNullOrWhiteSpace(stored)) return string.Empty;

        if (protector is null) return stored.Trim();

        try
        {
            return protector.Unprotect(stored).Trim();
        }
        catch (Exception ex)
        {
            // The protector failed (corrupt DPAPI blob, wrong user): report a missing key, never the ciphertext.
            Log.Warning("Cloud API key could not be decrypted ({ErrorType})", ex.GetType().Name);
            return string.Empty;
        }
    }

    private static EngineTranscription Failure(string errorCode, string? reason, string backend, string model) =>
        new("", "error", reason, errorCode, true, 0, 0, 0, 0, 0, 0, backend, model, 0);

    private static string BackendFor(string provider) => "cloud-" + provider;

    public ValueTask DisposeAsync() => local.DisposeAsync();
}
