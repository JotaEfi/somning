using Serilog;
using Somning.App.Logging;
using Somning.Core.Dictation;
using Xunit;

namespace Somning.Integration.Tests.Logging;

public sealed class MetricsLogTests
{
    [Fact]
    public void Metrics_line_contains_every_schema_field_and_no_secrets_or_transcript()
    {
        var logsDir = Path.Combine(Path.GetTempPath(), "somning-logs", Guid.NewGuid().ToString("N"));
        const string transcript = "Hoje vamos revisar o contrato do cliente.";
        Assert.Equal(41, transcript.Length);
        try
        {
            LoggingSetup.Initialize(logsDir);
            var metrics = new DictationMetrics("20261023-101500-123", "tap", 5000, 40, 12, 3, 900, 1500, 250,
                "vulkan", "ggml-large-v3-turbo-q5_0.bin", 30, 1300, "pasted", null, transcript.Length);

            MetricsLogger.Write(metrics);
            Log.Information("{@Cfg}", new { ApiKey = "sk-live-123", AccessToken = "tok-9", Authorization = "Bearer x", Model = "f16" });
            Log.CloseAndFlush();

            var content = string.Join("\n", Directory.EnumerateFiles(logsDir, "*.log").Select(ReadShared));
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var metricLine = Assert.Single(lines, line => line.Contains("dictation", StringComparison.Ordinal));
            foreach (var field in DictationMetrics.FieldNames)
                Assert.Contains(field, metricLine, StringComparison.Ordinal);
            Assert.Contains("load_ms", metricLine, StringComparison.Ordinal);
            Assert.Contains("warm_ms", metricLine, StringComparison.Ordinal);

            Assert.DoesNotContain("sk-live-123", content, StringComparison.Ordinal);
            Assert.DoesNotContain("tok-9", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer x", content, StringComparison.Ordinal);
            Assert.Contains("***", content, StringComparison.Ordinal);
            Assert.Contains("f16", content, StringComparison.Ordinal);
            Assert.DoesNotContain(transcript, content, StringComparison.Ordinal);
        }
        finally
        {
            Log.CloseAndFlush();
            if (Directory.Exists(logsDir)) Directory.Delete(logsDir, recursive: true);
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
