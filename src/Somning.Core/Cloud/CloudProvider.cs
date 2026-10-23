namespace Somning.Core.Cloud;

/// <summary>
/// Cloud STT providers supported by Somning (STT-05). The values match
/// <c>UserSettings.CloudProvider</c>, so settings and the router share one vocabulary.
/// Cloud transcription is always opt-in: <see cref="None"/> keeps the local engine.
/// </summary>
public static class CloudProvider
{
    public const string None = "none";
    public const string Groq = "groq";
    public const string OpenAi = "openai";

    public const string GroqModel = "whisper-large-v3-turbo";
    public const string OpenAiModel = "whisper-1";

    public const string GroqEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
    public const string OpenAiEndpoint = "https://api.openai.com/v1/audio/transcriptions";

    /// <summary>Trims and lowercases a provider name; null/blank collapses to <see cref="None"/>.</summary>
    public static string Normalize(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? None : provider.Trim().ToLowerInvariant();

    /// <summary>True for a provider that performs cloud transcription.</summary>
    public static bool IsCloud(string? provider) => Normalize(provider) is Groq or OpenAi;

    /// <summary>Model used when the user does not override it.</summary>
    public static string DefaultModel(string provider) => Normalize(provider) switch
    {
        Groq => GroqModel,
        OpenAi => OpenAiModel,
        _ => throw new ArgumentException($"Provider '{provider}' has no cloud model.", nameof(provider))
    };

    /// <summary>Transcription endpoint used when the user does not override it.</summary>
    public static string DefaultEndpoint(string provider) => Normalize(provider) switch
    {
        Groq => GroqEndpoint,
        OpenAi => OpenAiEndpoint,
        _ => throw new ArgumentException($"Provider '{provider}' has no cloud endpoint.", nameof(provider))
    };
}
