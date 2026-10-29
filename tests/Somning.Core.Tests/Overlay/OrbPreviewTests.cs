using System.IO.Compression;
using Somning.Core.Overlay;
using Xunit;

namespace Somning.Core.Tests.Overlay;

/// <summary>
/// Headless visual preview of the orb. Does nothing unless SOMNING_PREVIEW_DIR is set, so normal test runs stay fast.
/// Writes contact sheets (dark and light backdrops) plus 3x close-ups as PNG for visual inspection.
/// </summary>
public sealed class OrbPreviewTests
{
    private const float Dt = 1f / 60f;

    private sealed record Shot(string Label, OrbFrame Frame);

    [Fact]
    public void Writes_preview_sequence_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("SOMNING_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);

        var shots = Capture();
        var renderer = new OrbRenderer();
        WriteSheet(Path.Combine(dir, "sheet-dark.png"), renderer, shots, 1f, (24, 26, 33));
        WriteSheet(Path.Combine(dir, "sheet-light.png"), renderer, shots, 1f, (236, 238, 242));

        var direct = new OrbRenderer { AllowUpscale = false };
        WriteSingle(Path.Combine(dir, "upscaled-2x-07-voz-alta.png"), renderer, shots.First(s => s.Label == "07-voz-alta").Frame, 2f, (24, 26, 33));
        var close = new[] { "02-enter-overshoot", "05-silence", "07-voz-alta", "09-transcrevendo", "10-erro", "12-sumindo" };
        foreach (var shot in shots.Where(s => close.Contains(s.Label)))
        {
            WriteSingle(Path.Combine(dir, $"close-{shot.Label}.png"), direct, shot.Frame, 3f, (24, 26, 33));
        }
    }

    [Fact]
    public void Measures_render_cost_when_requested()
    {
        if (Environment.GetEnvironmentVariable("SOMNING_PERF") is not { Length: > 0 }) return;
        foreach (var dpi in new[] { 1f, 1.5f, 2f })
        {
            var layout = OrbLayout.Scaled(dpi);
            var buffer = new uint[layout.Width * layout.Height];
            var renderer = new OrbRenderer();
            var animator = new OrbAnimator();
            animator.Show(OverlayMode.Listening);
            animator.SetLevel(0.8f);
            for (var i = 0; i < 60; i++) animator.Tick(Dt);
            for (var i = 0; i < 30; i++) renderer.Render(buffer, layout.Width, layout.Height, dpi, animator.Frame, 120);
            const int frames = 300;
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (var i = 0; i < frames; i++)
            {
                animator.Tick(Dt);
                renderer.Render(buffer, layout.Width, layout.Height, dpi, animator.Frame, 120);
            }
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds / frames;
            Console.WriteLine($"PERF dpi={dpi}: {ms:F3} ms/frame = {ms * 60 / 10:F1}% of one core at 60 fps");
        }
    }

    private static List<Shot> Capture()
    {
        var animator = new OrbAnimator();
        var normalizer = new VoiceLevelNormalizer();
        var shots = new List<Shot>();
        var time = 0f;

        void Run(float seconds, Func<float, float>? rms = null)
        {
            var steps = (int)MathF.Round(seconds / Dt);
            for (var i = 0; i < steps; i++)
            {
                time += Dt;
                if (rms is not null) animator.SetLevel(normalizer.Normalize(rms(time), Dt));
                animator.Tick(Dt);
            }
        }

        animator.Show(OverlayMode.Listening);
        Run(0.05f); shots.Add(new Shot("01-enter-inicio", animator.Frame));
        Run(0.12f); shots.Add(new Shot("02-enter-overshoot", animator.Frame));
        Run(0.15f); shots.Add(new Shot("03-enter-assenta", animator.Frame));
        Run(1.2f, _ => 0.002f); shots.Add(new Shot("04-silencio-a", animator.Frame));
        Run(1.1f, _ => 0.002f); shots.Add(new Shot("05-silence", animator.Frame));
        Run(0.6f, t => 0.05f * (0.7f + 0.3f * MathF.Sin(t * 9f))); shots.Add(new Shot("06-voz-normal", animator.Frame));
        Run(0.4f, t => 0.20f * (0.8f + 0.2f * MathF.Sin(t * 11f))); shots.Add(new Shot("07-voz-alta", animator.Frame));
        Run(0.25f, t => 0.12f * (0.6f + 0.4f * MathF.Sin(t * 13f))); shots.Add(new Shot("08-voz-media", animator.Frame));
        animator.Show(OverlayMode.Transcribing);
        Run(0.9f); shots.Add(new Shot("09-transcrevendo", animator.Frame));
        Run(0.7f); shots.Add(new Shot("09b-transcrevendo", animator.Frame));
        animator.Show(OverlayMode.Notice);
        Run(0.8f); shots.Add(new Shot("10-erro", animator.Frame));
        animator.Hide();
        Run(0.05f); shots.Add(new Shot("11-saindo", animator.Frame));
        Run(0.08f); shots.Add(new Shot("12-sumindo", animator.Frame));
        Run(0.12f); shots.Add(new Shot("13-quase-fora", animator.Frame));
        return shots;
    }

    private static void WriteSheet(string path, OrbRenderer renderer, List<Shot> shots, float dpi, (int R, int G, int B) bg)
    {
        var layout = OrbLayout.Scaled(dpi);
        var columns = 5;
        var rows = (shots.Count + columns - 1) / columns;
        var sheetW = columns * layout.Width;
        var sheetH = rows * layout.Height;
        var sheet = new byte[sheetW * sheetH * 3];
        var tile = new uint[layout.Width * layout.Height];

        for (var i = 0; i < shots.Count; i++)
        {
            renderer.Render(tile, layout.Width, layout.Height, dpi, shots[i].Frame);
            PngWriter.Composite(sheet, sheetW, (i % columns) * layout.Width, (i / columns) * layout.Height, tile, layout.Width, layout.Height, bg);
        }

        PngWriter.WriteRgb(path, sheet, sheetW, sheetH);
    }

    private static void WriteSingle(string path, OrbRenderer renderer, OrbFrame frame, float dpi, (int R, int G, int B) bg)
    {
        var layout = OrbLayout.Scaled(dpi);
        var tile = new uint[layout.Width * layout.Height];
        var rgb = new byte[layout.Width * layout.Height * 3];
        renderer.Render(tile, layout.Width, layout.Height, dpi, frame);
        PngWriter.Composite(rgb, layout.Width, 0, 0, tile, layout.Width, layout.Height, bg);
        PngWriter.WriteRgb(path, rgb, layout.Width, layout.Height);
    }
}
