using System.Text.Json;

namespace Somning.Integration.Tests.Soak;

/// <summary>Raw measurements collected by the soak run (baseline = after warm-up iteration 3).</summary>
public sealed record SoakMeasurements(
    int Requested, int Completed, int Failures, double DurationSeconds,
    long BaselineHandles, long FinalHandles, long BaselinePrivateBytes, long FinalPrivateBytes,
    int HookInstallCountStart, int HookInstallCountEnd, int WatchdogReinstalls, bool FinalRealHotkeyOk);

/// <summary>Soak result: measurements plus the pure pass/fail verdict. Never hand-edited; produced by <see cref="Evaluate"/>.</summary>
public sealed record SoakReport(
    int Requested, int Completed, int Failures, double DurationSeconds,
    long BaselineHandles, long FinalHandles, long BaselinePrivateBytes, long FinalPrivateBytes,
    int HookInstallCountStart, int HookInstallCountEnd, int WatchdogReinstalls, bool FinalRealHotkeyOk,
    string Result, IReadOnlyList<string> Reasons)
{
    public const long MaxHandleGrowth = 200;
    public const long MaxPrivateByteGrowth = 150L * 1024 * 1024;
    public const string Pass = "PASS";
    public const string Fail = "FAIL";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public bool Passed => Result == Pass;

    public static SoakReport Evaluate(SoakMeasurements m)
    {
        var reasons = new List<string>();
        if (m.Requested < 1) reasons.Add($"requested must be >= 1 (was {m.Requested})");
        if (m.Failures != 0) reasons.Add($"failures must be 0 (was {m.Failures})");
        if (m.Completed != m.Requested) reasons.Add($"completed {m.Completed} != requested {m.Requested}");
        if (!m.FinalRealHotkeyOk) reasons.Add("final real-hotkey dictation did not succeed");
        if (m.HookInstallCountEnd < m.HookInstallCountStart)
            reasons.Add($"hook install count decreased ({m.HookInstallCountStart} -> {m.HookInstallCountEnd})");
        if (HasInvalidSample(m)) reasons.Add("invalid (negative or NaN) sample");
        else
        {
            var handleGrowth = m.FinalHandles - m.BaselineHandles;
            if (handleGrowth > MaxHandleGrowth) reasons.Add($"handle growth {handleGrowth} exceeds {MaxHandleGrowth}");
            var byteGrowth = m.FinalPrivateBytes - m.BaselinePrivateBytes;
            if (byteGrowth > MaxPrivateByteGrowth) reasons.Add($"private byte growth {byteGrowth} exceeds {MaxPrivateByteGrowth}");
        }

        return new SoakReport(m.Requested, m.Completed, m.Failures, m.DurationSeconds, m.BaselineHandles, m.FinalHandles,
            m.BaselinePrivateBytes, m.FinalPrivateBytes, m.HookInstallCountStart, m.HookInstallCountEnd, m.WatchdogReinstalls,
            m.FinalRealHotkeyOk, reasons.Count == 0 ? Pass : Fail, reasons);
    }

    private static bool HasInvalidSample(SoakMeasurements m) =>
        double.IsNaN(m.DurationSeconds) || m.DurationSeconds < 0 ||
        m.BaselineHandles < 0 || m.FinalHandles < 0 || m.BaselinePrivateBytes < 0 || m.FinalPrivateBytes < 0 ||
        m.HookInstallCountStart < 0 || m.HookInstallCountEnd < 0 || m.WatchdogReinstalls < 0 ||
        m.Completed < 0 || m.Failures < 0;

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
