using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Somning.Core.Engine;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Text;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Paste;

namespace Somning.Bench;

internal static class BenchRunner
{
    [STAThread]
    public static async Task<int> RunAsync(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        if (args.Length == 0 || args[0] != "run") { Console.Error.WriteLine("usage: Somning.Bench run --engine <exe> --fixtures <dir> --runs <n> --only <backend-model-fa> --results <jsonl>"); return 2; }
        var options = Parse(args[1..]);
        if (options is null) return 2;
        if (Process.GetProcessesByName("Somning.Engine").Length != 0 || Process.GetProcessesByName("Somning.App").Length != 0)
        { Console.Error.WriteLine("another Somning engine is running"); return 3; }
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("benchmark requires Windows"); return 2; }
        var (backend, modelKind, flash) = ParseConfig(options.Only);
        if (backend is null) { Console.Error.WriteLine("invalid --only; expected vulkan-f16-on"); return 2; }
        var engine = Path.GetFullPath(options.Engine);
        var fixtures = Path.GetFullPath(options.Fixtures);
        var modelFile = modelKind switch { "f16" => "ggml-large-v3-turbo.bin", "q8_0" => "ggml-large-v3-turbo-q8_0.bin", _ => "ggml-large-v3-turbo-q5_0.bin" };
        var model = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Somning", "models", modelFile);
        var vad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Somning", "models", EngineDefaults.VadModelFile);
        var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixtures, "manifest.json")));
        var rows = manifest.RootElement.EnumerateArray().Where(x => x.GetProperty("clean").GetBoolean()).ToArray();
        var longFixture = Path.Combine(fixtures, "speech", "longo-numeros.wav");
        if (!File.Exists(engine) || !File.Exists(model) || !File.Exists(vad) || !File.Exists(longFixture))
        { Console.Error.WriteLine("engine, pinned model, VAD model, or 15 s fixture missing"); return 1; }
        var stage = Path.Combine(Path.GetTempPath(), "somning-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(stage, "fixtures"));
        CopyDirectory(fixtures, Path.Combine(stage, "fixtures"));
        BenchEngineClient? client = null;
        try
        {
            // Refuse again immediately before starting the published worker.
            if (Process.GetProcessesByName("Somning.Engine").Length != 0 || Process.GetProcessesByName("Somning.App").Length != 0)
            { Console.Error.WriteLine("another Somning engine is running"); return 3; }
            var started = await BenchEngineClient.StartAsync(engine, stage, backend, model, vad, flash);
            if (started.Client is null) { Console.Error.WriteLine(started.Diagnostic); return 1; }
            client = started.Client;
            var ready = started.Ready!;
            // Discard one warm-up inference before measured runs.
            await client.TranscribeAsync("warmup", Path.Combine(stage, "fixtures", "speech", "longo-numeros.wav"));
            var accuracy = new List<object>();
            foreach (var row in rows)
            {
                var relative = row.GetProperty("file").GetString()!;
                var wav = Path.Combine(stage, "fixtures", relative.Replace('/', Path.DirectorySeparatorChar));
                var result = await client.TranscribeAsync("clean-" + accuracy.Count, wav);
                var expected = row.GetProperty("expected").GetString()!;
                accuracy.Add(new { file = relative, expected, transcript = result.Text, wer = Wer.Compute(expected, result.Text) });
            }
            using var pasteTarget = await OwnedEditor.StartAsync();
            var pasteSamples = new List<double>();
            var inferSamples = new List<double>();
            var requestSamples = new List<object>();
            for (var i = 0; i < options.Runs; i++)
            {
                var handoff = Stopwatch.GetTimestamp();
                var result = await client.TranscribeAsync("run-" + i, Path.Combine(stage, "fixtures", "speech", "longo-numeros.wav"));
                var requestResultMs = Stopwatch.GetElapsedTime(handoff).TotalMilliseconds;
                inferSamples.Add(result.InferMs);
                pasteTarget.ClearAndActivate();
                var target = new ForegroundProbe(new IntegrityReader()).Capture();
                var pasteStart = Stopwatch.GetTimestamp();
                var delivery = await pasteTarget.DeliverAsync(result.Text, target, CancellationToken.None);
                if (delivery.Outcome != "pasted" || !pasteTarget.WaitForText(result.Text, TimeSpan.FromSeconds(2)))
                    throw new InvalidOperationException("benchmark-owned editor paste verification failed: " + delivery.Outcome + "; reason=" + delivery.Reason + "; actual=" + pasteTarget.GetText());
                pasteSamples.Add(Stopwatch.GetElapsedTime(handoff).TotalMilliseconds);
                requestSamples.Add(new { request_to_result_ms = requestResultMs, clipboard_ms = delivery.ClipboardMs, paste_ms = delivery.PasteMs, stop_to_paste_ms = Stopwatch.GetElapsedTime(handoff).TotalMilliseconds });
            }
            var record = new
            {
                config = options.Only, backend, model = modelKind, flash_attention = flash, runs = options.Runs,
                load_ms = ready.LoadMs, warm_ms = ready.WarmMs, gpu_in_use = ready.GpuInUse,
                infer_ms = Stats(inferSamples), stop_to_paste_ms = Stats(pasteSamples), samples = requestSamples,
                clean_fixtures = accuracy, mean_clean_wer = accuracy.Average(x => (double)x.GetType().GetProperty("wer")!.GetValue(x)!),
                transcript = ((dynamic)accuracy[0]).transcript,
                machine = new { machine_name = Environment.MachineName, os = Environment.OSVersion.VersionString, processor_count = Environment.ProcessorCount },
                revision = GitRevision(), engine_sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(engine))).ToLowerInvariant(),
                measured_at_utc = DateTimeOffset.UtcNow
            };
            var resultPath = Path.GetFullPath(options.Results);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            await File.AppendAllTextAsync(resultPath, JsonSerializer.Serialize(record) + "\n", new UTF8Encoding(false));
            Console.WriteLine($"wrote {options.Only}: {options.Runs} runs; stop-to-paste p50={Stats(pasteSamples).p50_ms:F2} ms p95={Stats(pasteSamples).p95_ms:F2} ms");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { if (client is not null) await client.DisposeAsync(); Directory.Delete(stage, true); }
    }

    private static (string? Backend, string Model, bool Flash) ParseConfig(string text)
    {
        var p = text.Split('-'); if (p.Length != 3 || p[0] is not ("vulkan" or "cpu") || p[1] is not ("f16" or "q8_0" or "q5_0") || p[2] is not ("on" or "off")) return (null, "", false);
        return (p[0], p[1], p[2] == "on");
    }
    private sealed record Percentiles(double p50_ms, double p95_ms);
    private static Percentiles Stats(List<double> values) { var sorted = values.Order().ToArray(); return new(Nearest(sorted, .50), Nearest(sorted, .95)); }
    private static double Nearest(double[] x, double p) => x[Math.Max(0, (int)Math.Ceiling(p * x.Length) - 1)];
    private static string GitRevision() { try { using var p = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false }); return p!.StandardOutput.ReadToEnd().Trim(); } catch { return "unknown"; } }
    private static void CopyDirectory(string from, string to) { foreach (var d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, d))); foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories)) { var target = Path.Combine(to, Path.GetRelativePath(from, f)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(f, target, true); } }
    private sealed record Args(string Engine, string Fixtures, int Runs, string Only, string Results);
    private static Args? Parse(string[] args) { var d = new Dictionary<string,string>(); for (int i=0;i<args.Length;i+=2) { if (i+1>=args.Length || !d.TryAdd(args[i],args[i+1])) return null; } if (!d.TryGetValue("--engine",out var e)||!d.TryGetValue("--fixtures",out var f)||!d.TryGetValue("--runs",out var n)||!int.TryParse(n,out var runs)||runs<10||!d.TryGetValue("--only",out var o)||!d.TryGetValue("--results",out var r)) return null; return new(e,f,runs,o,r); }
}
