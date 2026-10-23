using System.Text.Json.Serialization;

namespace Somning.Core.Cloud;

/// <summary>
/// One cloud transcription attempt (STT-05): which provider to call, the user-supplied key and the
/// optional vocabulary/endpoint overrides. <see cref="ToString"/> never exposes the plaintext key
/// (APP-06), so the record can be logged as-is.
/// </summary>
public sealed record CloudEngineConfig
{
    public CloudEngineConfig(
        string provider,
        string apiKey,
        string? customVocabulary = null,
        string? customEndpoint = null)
    {
        Provider = CloudProvider.Normalize(provider);
        ApiKey = apiKey ?? string.Empty;
        CustomVocabulary = string.IsNullOrWhiteSpace(customVocabulary) ? null : customVocabulary.Trim();
        CustomEndpoint = string.IsNullOrWhiteSpace(customEndpoint) ? null : customEndpoint.Trim();
    }

    /// <summary>One of <see cref="CloudProvider"/>'s values, normalized to lowercase.</summary>
    public string Provider { get; init; }

    /// <summary>Plaintext key. Never logged; use <see cref="ApiKeyMasker"/> for any diagnostic output.</summary>
    [JsonIgnore]
    public string ApiKey { get; init; }

    /// <summary>Vocabulary/prompt bias appended to the multipart payload when present.</summary>
    public string? CustomVocabulary { get; init; }

    /// <summary>Overrides the provider's default endpoint (useful for tests and proxies).</summary>
    public string? CustomEndpoint { get; init; }

    /// <summary>Masked key, safe for logs and exception messages.</summary>
    public string MaskedApiKey => ApiKeyMasker.Mask(ApiKey);

    /// <summary>True when the configuration carries a non-blank key.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Request model: the provider default, or the explicit model name from the settings.</summary>
    public string ResolveModel() => CloudProvider.DefaultModel(Provider);

    /// <summary>Absolute endpoint for this attempt.</summary>
    public Uri ResolveEndpoint() => new(CustomEndpoint ?? CloudProvider.DefaultEndpoint(Provider), UriKind.Absolute);

    /// <summary>Validates that this configuration can be sent to a cloud provider.</summary>
    public void Validate()
    {
        if (!CloudProvider.IsCloud(Provider))
            throw new ArgumentException($"Provider '{Provider}' does not support cloud transcription.", nameof(Provider));
        if (!HasApiKey)
            throw new ArgumentException("A cloud API key is required.", nameof(ApiKey));
    }

    public override string ToString() =>
        $"CloudEngineConfig {{ Provider = {Provider}, ApiKey = {MaskedApiKey}, " +
        $"CustomVocabulary = {(CustomVocabulary is null ? "null" : "set")}, " +
        $"CustomEndpoint = {CustomEndpoint ?? "default"} }}";
}
