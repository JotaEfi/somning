using System.Globalization;
using System.Text.Json;
using Somning.Core.Engine;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.E2E;

[Trait("Category", "E2E")]
public sealed class DictationE2ETests
{
    [Fact]
    public async Task TapTap_cmd_curto_delivers_to_notepad_without_layout_or_hotkey_leaks()
    {
        using var e2e = await E2EFixtureSupport.StartAsync(E2EFixtureSupport.Fixture("speech/cmd-curto.wav"));
        var outcome = await e2e.DictateAsync(E2EFixtureSupport.Fixture("speech/cmd-curto.wav"), hold: false);
        Assert.Equal("tap", outcome.Metrics?["mode"]?.GetValue<string>());
        e2e.AssertPasted(outcome, E2EFixtureSupport.Expected("speech/cmd-curto.txt"));
        e2e.AssertLayoutUnchanged();
    }

    [Fact]
    public async Task HoldRelease_longo_numeros_requires_full_hold_and_delivers()
    {
        var fixture = E2EFixtureSupport.Fixture("speech/longo-numeros.wav");
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        var outcome = await e2e.DictateAsync(fixture, hold: true);
        Assert.Equal("hold", outcome.Metrics?["mode"]?.GetValue<string>());
        e2e.AssertPasted(outcome, E2EFixtureSupport.Expected("speech/longo-numeros.txt"));
        e2e.AssertLayoutUnchanged();
    }

    [Fact]
    public async Task Repeated_dictations_measure_warm_stop_to_paste_budget()
    {
        var fixture = E2EFixtureSupport.Fixture("speech/longo-numeros.wav");
        using var e2e = await E2EFixtureSupport.StartAsync(fixture);
        var measurements = new List<long>();
        var cursor = await e2e.App.LastSeqAsync();
        string backend = "unknown";
        for (var i = 0; i < 10; i++)
        {
            e2e.Notepad.SetText(string.Empty);
            var outcome = await e2e.DictateAsync(fixture, hold: false, cursor);
            cursor = outcome.Seq;
            backend = outcome.Metrics?["backend"]?.GetValue<string>() ?? "unknown";
            e2e.AssertPasted(outcome, E2EFixtureSupport.Expected("speech/longo-numeros.txt"));
            measurements.Add(outcome.MetricLong("stop_to_paste_ms"));
        }
        var sorted = measurements.Order().ToArray();
        var p50 = sorted[(int)Math.Ceiling(.50 * sorted.Length) - 1];
        var p95 = sorted[(int)Math.Ceiling(.95 * sorted.Length) - 1];
        var evidence = new { selected = EngineDefaults.Backend + "-" + EngineDefaults.ModelFile + "-fa=" + EngineDefaults.FlashAttention, samples_ms = measurements, p50_ms = p50, p95_ms = p95, budget_ms = EngineDefaults.BudgetP95StopToPasteMs, backend, measured_at_utc = DateTimeOffset.UtcNow };
        var evidencePath = Path.Combine(RepoPaths.Root, ".planning", "phases", "01-dictation-loop-mvp", "01-12-latency.json");
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), TestContext.Current.CancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(evidence));
        Assert.True(p95 <= EngineDefaults.BudgetP95StopToPasteMs,
            $"warm stop-to-paste p95 {p95.ToString(CultureInfo.InvariantCulture)} ms exceeds selected budget {EngineDefaults.BudgetP95StopToPasteMs} ms; samples={string.Join(',', measurements)}");
        e2e.AssertLayoutUnchanged();
    }

}
