using System.Globalization;
using System.Text;
using System.Text.Json;
using Serilog;
using Somning.Core.Dictation;

namespace Somning.App.Logging;

/// <summary>
/// Writes exactly one structured line per dictation (APP-06, D-23). Every DictationMetrics.FieldNames entry is
/// attached as a scalar property and rendered as compact JSON; the transcript text is never logged (only chars).
/// </summary>
public static class MetricsLogger
{
    public static void Write(DictationMetrics metrics) => Write(Log.Logger, metrics);

    public static void Write(ILogger logger, DictationMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metrics);
        var fields = metrics.ToFields();
        var enriched = logger;
        foreach (var name in DictationMetrics.FieldNames)
            enriched = enriched.ForContext(name, fields[name]);
        enriched.Information("dictation {Id} {Mode} {Outcome} {Fields:l}", metrics.Id, metrics.Mode, metrics.Outcome, ToJson(fields));
    }

    public static string ToJson(IReadOnlyDictionary<string, object?> fields)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var name in DictationMetrics.FieldNames)
                WriteValue(writer, name, fields[name]);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNull(name); break;
            case long number: writer.WriteNumber(name, number); break;
            case int number: writer.WriteNumber(name, number); break;
            default: writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }
}
