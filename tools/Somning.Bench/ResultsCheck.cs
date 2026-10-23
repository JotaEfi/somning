using System.Globalization;
using System.Text.Json;
using Somning.Core.Engine;

namespace Somning.Bench;

internal static class ResultsCheck
{
    public static async Task<int> RunAsync(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        var options = Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine("usage: Somning.Bench check --results <jsonl> --report <BENCHMARK.md>");
            return 2;
        }

        var resultsPath = Path.GetFullPath(options.Results);
        var reportPath = Path.GetFullPath(options.Report);
        if (!File.Exists(resultsPath))
        {
            Console.Error.WriteLine($"results file '{options.Results}' not found");
            return 1;
        }
        if (!File.Exists(reportPath))
        {
            Console.Error.WriteLine($"report file '{options.Report}' not found");
            return 1;
        }

        // 1. Parse results JSONL
        var recordsByConfig = new Dictionary<string, (JsonElement Root, double[] StopSamples, double RecomputedP50, double RecomputedP95, double RecomputedWer)>();
        foreach (var line in await File.ReadAllLinesAsync(resultsPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("config", out var configProp)) continue;
            var config = configProp.GetString()!;

            // Reject missing-model diagnostic records
            if (root.TryGetProperty("diagnostic", out var diag) && !string.IsNullOrEmpty(diag.GetString()))
            {
                Console.Error.WriteLine($"error: record '{config}' contains diagnostic '{diag.GetString()}', matrix incomplete");
                return 1;
            }

            var runs = root.GetProperty("runs").GetInt32();
            if (runs < 10)
            {
                Console.Error.WriteLine($"error: config '{config}' has {runs} runs, expected >= 10");
                return 1;
            }

            var samples = root.GetProperty("samples").EnumerateArray().ToList();
            if (samples.Count < 10)
            {
                Console.Error.WriteLine($"error: config '{config}' has {samples.Count} samples, expected >= 10");
                return 1;
            }

            var stopValues = new List<double>();
            foreach (var s in samples)
            {
                var req = s.GetProperty("request_to_result_ms").GetDouble();
                var clip = s.GetProperty("clipboard_ms").GetDouble();
                var paste = s.GetProperty("paste_ms").GetDouble();
                var stop = s.GetProperty("stop_to_paste_ms").GetDouble();

                if (double.IsNaN(req) || double.IsInfinity(req) || req < 0 ||
                    double.IsNaN(clip) || double.IsInfinity(clip) || clip < 0 ||
                    double.IsNaN(paste) || double.IsInfinity(paste) || paste < 0 ||
                    double.IsNaN(stop) || double.IsInfinity(stop) || stop < 0)
                {
                    Console.Error.WriteLine($"error: config '{config}' contains invalid non-finite or negative timing sample");
                    return 1;
                }
                stopValues.Add(stop);
            }

            var sortedStops = stopValues.Order().ToArray();
            var recomputedP50 = Nearest(sortedStops, 0.50);
            var recomputedP95 = Nearest(sortedStops, 0.95);

            var cleanFixtures = root.GetProperty("clean_fixtures").EnumerateArray().ToList();
            if (cleanFixtures.Count == 0)
            {
                Console.Error.WriteLine($"error: config '{config}' has no clean fixtures");
                return 1;
            }
            var wers = cleanFixtures.Select(f => f.GetProperty("wer").GetDouble()).ToList();
            var recomputedWer = wers.Average();

            recordsByConfig[config] = (root.Clone(), sortedStops, recomputedP50, recomputedP95, recomputedWer);
        }

        // Verify all 12 configs present
        foreach (var requiredConfig in BenchRunner.AllConfigs)
        {
            if (!recordsByConfig.ContainsKey(requiredConfig))
            {
                Console.Error.WriteLine($"error: missing configuration '{requiredConfig}' in results; expected all 12 configs");
                return 1;
            }
        }

        // 2. Compute winner & CPU fallback
        var eligible = recordsByConfig
            .Where(kv => kv.Value.RecomputedP95 <= 3000.0)
            .ToList();

        if (eligible.Count == 0)
        {
            Console.Error.WriteLine("error: no configuration met the warm p95 stop-to-paste <= 3000 ms budget");
            return 1;
        }

        var winnerKv = eligible
            .OrderBy(kv => Math.Round(kv.Value.RecomputedWer, 3))
            .ThenBy(kv => kv.Value.RecomputedP95)
            .First();

        var winnerConfig = winnerKv.Key;
        var winnerData = winnerKv.Value;
        var winnerBackend = winnerData.Root.GetProperty("backend").GetString()!;
        var winnerModel = winnerData.Root.GetProperty("model").GetString()!;
        var winnerFlash = winnerData.Root.GetProperty("flash_attention").GetBoolean();

        var cpuFallbackKv = recordsByConfig
            .Where(kv => kv.Value.Root.GetProperty("backend").GetString() == "cpu" && kv.Value.RecomputedWer <= 0.10)
            .OrderBy(kv => kv.Value.RecomputedP95)
            .FirstOrDefault();

        if (cpuFallbackKv.Key is null)
        {
            Console.Error.WriteLine("error: no CPU fallback met the mean clean WER <= 0.10 threshold");
            return 1;
        }

        var cpuFallbackConfig = cpuFallbackKv.Key;
        var cpuFallbackModel = cpuFallbackKv.Value.Root.GetProperty("model").GetString()!;

        Console.WriteLine($"Computed winner: {winnerConfig} (p95={winnerData.RecomputedP95:F2} ms, WER={winnerData.RecomputedWer * 100:F2}%)");
        Console.WriteLine($"Computed CPU fallback: {cpuFallbackConfig} (p95={cpuFallbackKv.Value.RecomputedP95:F2} ms, WER={cpuFallbackKv.Value.RecomputedWer * 100:F2}%)");

        // 3. Verify report file contents
        var reportText = await File.ReadAllTextAsync(reportPath);
        if (!reportText.Contains("## Selected"))
        {
            Console.Error.WriteLine("error: report missing '## Selected' heading");
            return 1;
        }
        if (!reportText.Contains(winnerConfig))
        {
            Console.Error.WriteLine($"error: report does not name selected winner config '{winnerConfig}'");
            return 1;
        }
        if (!reportText.Contains(cpuFallbackConfig))
        {
            Console.Error.WriteLine($"error: report does not name CPU fallback config '{cpuFallbackConfig}'");
            return 1;
        }
        if (!reportText.Contains("p95 stop-to-paste <= 3.0 s for a 15 s utterance on the selected config"))
        {
            Console.Error.WriteLine("error: report does not state required STT-08 budget text");
            return 1;
        }
        if (!reportText.Contains("MSVCP140") || !reportText.Contains("vulkan-1.dll"))
        {
            Console.Error.WriteLine("error: report does not include deployment prerequisites note");
            return 1;
        }

        // 4. Verify EngineDefaults matches
        var defaultsWinnerModelFile = ReportWriter.GetModelFile(winnerModel);
        var defaultsFallbackModelFile = ReportWriter.GetModelFile(cpuFallbackModel);

        if (EngineDefaults.Backend != winnerBackend)
        {
            Console.Error.WriteLine($"error: EngineDefaults.Backend ('{EngineDefaults.Backend}') does not match selected '{winnerBackend}'");
            return 1;
        }
        if (EngineDefaults.ModelFile != defaultsWinnerModelFile)
        {
            Console.Error.WriteLine($"error: EngineDefaults.ModelFile ('{EngineDefaults.ModelFile}') does not match selected '{defaultsWinnerModelFile}'");
            return 1;
        }
        if (EngineDefaults.FlashAttention != winnerFlash)
        {
            Console.Error.WriteLine($"error: EngineDefaults.FlashAttention ({EngineDefaults.FlashAttention}) does not match selected {winnerFlash}");
            return 1;
        }
        if (EngineDefaults.CpuFallbackModelFile != defaultsFallbackModelFile)
        {
            Console.Error.WriteLine($"error: EngineDefaults.CpuFallbackModelFile ('{EngineDefaults.CpuFallbackModelFile}') does not match fallback '{defaultsFallbackModelFile}'");
            return 1;
        }
        var budget = (int)typeof(EngineDefaults).GetField(nameof(EngineDefaults.BudgetP95StopToPasteMs))!.GetValue(null)!;
        if (budget != 3000)
        {
            Console.Error.WriteLine($"error: EngineDefaults.BudgetP95StopToPasteMs ({budget}) != 3000");
            return 1;
        }

        // Verify comment in EngineDefaults.cs
        var engineDefaultsSource = await File.ReadAllTextAsync("src/Somning.Core/Engine/EngineDefaults.cs");
        if (!engineDefaultsSource.Contains("BENCHMARK.md"))
        {
            Console.Error.WriteLine("error: src/Somning.Core/Engine/EngineDefaults.cs does not cite BENCHMARK.md in comment");
            return 1;
        }

        Console.WriteLine("All benchmark results and consistency checks passed successfully (exit 0)!");
        return 0;
    }

    private static double Nearest(double[] x, double p) => x[Math.Max(0, (int)Math.Ceiling(p * x.Length) - 1)];

    private sealed record Args(string Results, string Report);
    private static Args? Parse(string[] args)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !d.TryAdd(args[i], args[i + 1])) return null;
        }
        if (!d.TryGetValue("--results", out var r) || !d.TryGetValue("--report", out var p)) return null;
        return new(r, p);
    }
}
