namespace Somning.Core.Output;

/// <summary>Keeps the last delivered transcript so the tray can copy/paste it again (OUT-05).</summary>
public interface ITranscriptHistory
{
    string? LastTranscript { get; }
    bool HasTranscript { get; }
    void Record(string text);
    void Clear();
}
