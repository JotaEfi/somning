using System.Text.Json;
using Xunit;

namespace Somning.Integration.Tests.Soak;

public sealed class SoakReportTests
{
    private static SoakMeasurements Good() => new(50, 50, 0, 120.5, 400, 450, 200_000_000, 250_000_000, 1, 1, 0, true);

    [Fact]
    public void Clean_run_within_limits_passes()
    {
        var report = SoakReport.Evaluate(Good());
        Assert.Equal("PASS", report.Result);
        Assert.Empty(report.Reasons);
        Assert.True(report.Passed);
    }

    [Fact]
    public void Single_failed_iteration_fails_with_reason()
    {
        var report = SoakReport.Evaluate(Good() with { Failures = 1 });
        Assert.Equal("FAIL", report.Result);
        Assert.Contains(report.Reasons, r => r.Contains("failures"));
    }

    [Fact]
    public void Incomplete_run_fails()
    {
        var report = SoakReport.Evaluate(Good() with { Completed = 49 });
        Assert.Equal("FAIL", report.Result);
        Assert.Contains(report.Reasons, r => r.Contains("completed"));
    }

    [Fact]
    public void Failed_final_real_hotkey_dictation_fails()
    {
        var report = SoakReport.Evaluate(Good() with { FinalRealHotkeyOk = false });
        Assert.Equal("FAIL", report.Result);
        Assert.Contains(report.Reasons, r => r.Contains("real-hotkey"));
    }

    [Fact]
    public void Decreasing_hook_install_count_fails()
    {
        var report = SoakReport.Evaluate(Good() with { HookInstallCountStart = 3, HookInstallCountEnd = 2 });
        Assert.Equal("FAIL", report.Result);
        Assert.Contains(report.Reasons, r => r.Contains("install count"));
    }

    [Fact]
    public void Increasing_hook_install_count_is_allowed()
    {
        Assert.Equal("PASS", SoakReport.Evaluate(Good() with { HookInstallCountEnd = 3, WatchdogReinstalls = 2 }).Result);
    }

    [Fact]
    public void Handle_growth_over_limit_fails_and_exact_limit_passes()
    {
        Assert.Equal("PASS", SoakReport.Evaluate(Good() with { FinalHandles = 400 + SoakReport.MaxHandleGrowth }).Result);
        var report = SoakReport.Evaluate(Good() with { FinalHandles = 400 + SoakReport.MaxHandleGrowth + 1 });
        Assert.Equal("FAIL", report.Result);
        Assert.Contains(report.Reasons, r => r.Contains("handle growth"));
    }

    [Fact]
    public void Private_byte_growth_over_limit_fails()
    {
        var report = SoakReport.Evaluate(Good() with { FinalPrivateBytes = 200_000_000 + SoakReport.MaxPrivateByteGrowth + 1 });
        Assert.Equal("FAIL", report.Result);
        Assert.Contains(report.Reasons, r => r.Contains("private byte"));
    }

    [Fact]
    public void Negative_or_nan_samples_fail()
    {
        Assert.Equal("FAIL", SoakReport.Evaluate(Good() with { FinalHandles = -1 }).Result);
        var nan = SoakReport.Evaluate(Good() with { DurationSeconds = double.NaN });
        Assert.Equal("FAIL", nan.Result);
        Assert.Contains(nan.Reasons, r => r.Contains("invalid"));
    }

    [Fact]
    public void Multiple_violations_are_all_listed()
    {
        var report = SoakReport.Evaluate(Good() with { Failures = 2, FinalRealHotkeyOk = false });
        Assert.True(report.Reasons.Count >= 2);
    }

    [Fact]
    public void Json_contains_required_fields()
    {
        using var doc = JsonDocument.Parse(SoakReport.Evaluate(Good()).ToJson());
        foreach (var name in new[] { "requested", "completed", "failures", "durationSeconds", "baselineHandles", "finalHandles",
            "baselinePrivateBytes", "finalPrivateBytes", "hookInstallCountStart", "hookInstallCountEnd", "watchdogReinstalls",
            "finalRealHotkeyOk", "result", "reasons" })
            Assert.True(doc.RootElement.TryGetProperty(name, out _), "missing " + name);
    }
}
