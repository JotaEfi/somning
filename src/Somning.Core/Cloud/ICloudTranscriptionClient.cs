namespace Somning.Core.Cloud;

/// <summary>Transcript returned by a cloud provider (STT-05).</summary>
/// <param name="Text">Recognized text; may be empty when the provider detected no speech.</param>
/// <param name="DurationMs">Audio duration read from the WAV header, or 0 when unknown.</param>
/// <param name="InferMs">Wall-clock time spent in the HTTP round trip.</param>
/// <param name="Model">Model that produced the transcript.</param>
public sealed record CloudTranscriptionResult(string Text, long DurationMs, long InferMs, string Model);

/// <summary>Sends a WAV file to a cloud STT provider (Groq or OpenAI) and returns the transcript.</summary>
public interface ICloudTranscriptionClient
{
    Task<CloudTranscriptionResult> TranscribeAsync(
        string wavPath,
        CloudEngineConfig config,
        CancellationToken cancellationToken = default);
}
