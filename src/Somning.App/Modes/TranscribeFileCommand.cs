using Serilog;
using Somning.App.Engine;
using Somning.Core.Engine;
using Somning.Core.Paths;
using Somning.Core.Text;
using Somning.Platform.Windows.Clipboard;
namespace Somning.App.Modes;
public static class TranscribeFileCommand
{
    public static async Task<int> RunAsync(string wavPath, string backend, string? enginePath = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(SomningPaths.RecordingsDir);
        var copied = Path.Combine(SomningPaths.RecordingsDir,
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".wav");
        File.Copy(wavPath, copied, overwrite: false);
        await using var supervisor = new EngineSupervisor(enginePath);
        await supervisor.StartAsync(backend, ct);
        var result = await supervisor.TranscribeAsync(copied, ct);
        string outcome; int exit;
        if (result.Outcome == "text")
        {
            var clipboard = TrySetText(TextNormalizer.ForPaste(result.Text));
            outcome = clipboard.Success ? "clipboard_set" : "clipboard_failed";
            exit = clipboard.Success ? 0 : 11;
        }
        else { outcome = result.Outcome; exit = result.Outcome == "empty" ? 10 : 1; }
        Log.Information("Dictation outcome={outcome} chars={chars} infer_ms={infer_ms} load_ms={load_ms} warm_ms={warm_ms} backend={backend} model={model}",
            outcome, result.Text.Length, result.InferMs, result.LoadMs, result.WarmMs, result.Backend, result.Model);
        return exit;
    }
    private static ClipboardSetResult TrySetText(string text)
    {
        try { using var clipboard = new ClipboardWriter(); return clipboard.SetText(text); }
        catch (Exception) { return new(false, 0, 0, 0, "clipboard_unavailable"); }
    }
}
