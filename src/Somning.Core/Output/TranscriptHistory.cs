namespace Somning.Core.Output;

/// <summary>Thread-safe in-memory history. Blank text never erases the previous valid transcript.</summary>
public sealed class TranscriptHistory : ITranscriptHistory
{
    private readonly object gate = new();
    private string? lastTranscript;

    public string? LastTranscript
    {
        get
        {
            lock (gate) return lastTranscript;
        }
    }

    public bool HasTranscript
    {
        get
        {
            lock (gate) return lastTranscript is not null;
        }
    }

    public void Record(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (gate) lastTranscript = text;
    }

    public void Clear()
    {
        lock (gate) lastTranscript = null;
    }
}
