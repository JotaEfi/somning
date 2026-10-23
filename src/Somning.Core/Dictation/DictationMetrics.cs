namespace Somning.Core.Dictation;

/// <summary>
/// One record per dictation (D-23, APP-06). The transcript itself is never part of the schema; only Chars.
/// Outcome is one of pasted | paste_deferred | empty | error | recovered.
/// </summary>
public sealed record DictationMetrics(
    string Id,
    string Mode,
    long CaptureMs,
    long MicOpenMs,
    long VadMs,
    long QueueMs,
    long InferMs,
    long LoadMs,
    long WarmMs,
    string Backend,
    string Model,
    long PasteMs,
    long StopToPasteMs,
    string Outcome,
    string? Reason,
    int Chars)
{
    /// <summary>The single authoritative schema, in field order. Logger and tests iterate this list.</summary>
    public static readonly IReadOnlyList<string> FieldNames = Array.AsReadOnly(new[]
    {
        "id", "mode", "capture_ms", "mic_open_ms", "vad_ms", "queue_ms", "infer_ms", "load_ms", "warm_ms",
        "backend", "model", "paste_ms", "stop_to_paste_ms", "outcome", "reason", "chars"
    });

    public IReadOnlyDictionary<string, object?> ToFields()
    {
        object?[] values =
        [
            Id, Mode, CaptureMs, MicOpenMs, VadMs, QueueMs, InferMs, LoadMs, WarmMs,
            Backend, Model, PasteMs, StopToPasteMs, Outcome, Reason, Chars
        ];
        var fields = new OrderedFieldMap(FieldNames.Count);
        for (var index = 0; index < FieldNames.Count; index++)
            fields.Add(FieldNames[index], values[index]);
        return fields;
    }

    /// <summary>Read-only map that enumerates keys in schema order.</summary>
    private sealed class OrderedFieldMap(int capacity) : IReadOnlyDictionary<string, object?>
    {
        private readonly List<KeyValuePair<string, object?>> entries = new(capacity);
        private readonly Dictionary<string, object?> lookup = new(capacity, StringComparer.Ordinal);

        public void Add(string key, object? value)
        {
            lookup.Add(key, value);
            entries.Add(new KeyValuePair<string, object?>(key, value));
        }

        public object? this[string key] => lookup[key];
        public IEnumerable<string> Keys => entries.Select(entry => entry.Key);
        public IEnumerable<object?> Values => entries.Select(entry => entry.Value);
        public int Count => entries.Count;
        public bool ContainsKey(string key) => lookup.ContainsKey(key);
        public bool TryGetValue(string key, out object? value) => lookup.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => entries.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
