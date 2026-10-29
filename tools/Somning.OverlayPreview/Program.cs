using System.Diagnostics;
using Somning.Core.Overlay;
using Somning.OverlayPreview;
using Somning.Platform.Windows.Overlay;

// Usage: Somning.OverlayPreview <output-dir> | bench
if (args.Length > 0 && args[0] == "bench")
{
    Bench.Run();
    return 0;
}

var outDir = args.Length > 0 ? args[0] : "preview";
Directory.CreateDirectory(outDir);
Scenarios.WriteAll(outDir);
Console.WriteLine($"Wrote previews to {Path.GetFullPath(outDir)}");
return 0;

namespace Somning.OverlayPreview
{
    internal static class Bench
    {
        public static void Run()
        {
            foreach (var (name, mode, level) in new[]
            {
                ("listening-loud", OverlayMode.Listening, 0.18f),
                ("listening-silence", OverlayMode.Listening, 0f),
                ("transcribing", OverlayMode.Transcribing, 0f),
                ("notice", OverlayMode.Notice, 0f)
            })
            {
                var (cpuMs, size) = Measure(mode, level, 1f);
                Console.WriteLine($"{name,-18} {size,-9} {cpuMs:F3} ms/frame  => {cpuMs / 16.667 * 100:F1}% of one core at 60 fps");
            }

            var (ms2, size2) = Measure(OverlayMode.Listening, 0.18f, 1.5f);
            Console.WriteLine($"listening-loud@150% {size2,-9} {ms2:F3} ms/frame  => {ms2 / 16.667 * 100:F1}% of one core at 60 fps");
        }

        private static (double Ms, string Size) Measure(OverlayMode mode, float level, float dpi)
        {
            var text = mode == OverlayMode.Notice ? TextMask.Create(OverlayText.For(OverlayNoticeKind.NothingDetected), (int)(15 * dpi)) : TextMask.Empty;
            var (w, h) = DropLayout.WindowSize(dpi, mode == OverlayMode.Notice, text.Width);
            var buffer = new uint[w * h];
            var renderer = new DropRenderer();
            var animator = new OverlayAnimator();
            animator.Show(mode);
            var half = DropLayout.NoticeHalfWidth(text.Width, dpi);
            for (var i = 0; i < 120; i++)
            {
                animator.SetLevel(level * (0.6f + 0.4f * MathF.Sin(i * 0.4f)));
                animator.Tick(1f / 60f);
            }

            const int frames = 1200;
            for (var i = 0; i < 600; i++) renderer.Render(buffer, w, h, dpi, animator.Frame, text, half);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++)
            {
                animator.SetLevel(level * (0.6f + 0.4f * MathF.Sin(i * 0.4f)));
                animator.Tick(1f / 60f);
                renderer.Render(buffer, w, h, dpi, animator.Frame, text, half);
            }

            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds / frames, $"{w}x{h}");
        }
    }
}
