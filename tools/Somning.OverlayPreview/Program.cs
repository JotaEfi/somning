using System.Diagnostics;
using Somning.Core.Overlay;
using Somning.OverlayPreview;
using Somning.Platform.Windows.Overlay;

// Usage: Somning.OverlayPreview <output-dir> | bench
if (args.Length > 0 && args[0] == "bench")
{
    Bench.Run(args.Length > 1 ? args[1] : "all");
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
        public static void Run(string which)
        {
            if (which is "quick") { Console.WriteLine(Measure(OverlayMode.Listening, 0f, 1f, true).Ms); Console.WriteLine(Measure(OverlayMode.Listening, 0.18f, 1f, true).Ms); return; }
            if (which is "all" or "render") RunRender();
            if (which is "all" or "capture") MeasureCapture();
        }

        private static void RunRender()
        {
            Console.WriteLine("render + backdrop cache per frame (60 fps), capture amortized at 15 fps, single thread");
            foreach (var dpi in new[] { 1f, 1.5f, 2f })
            {
                foreach (var (name, mode, level) in new[]
                {
                    ("listening-loud", OverlayMode.Listening, 0.18f),
                    ("listening-silence", OverlayMode.Listening, 0f),
                    ("transcribing", OverlayMode.Transcribing, 0f),
                    ("notice", OverlayMode.Notice, 0f)
                })
                {
                    var plain = Measure(mode, level, dpi, useBackdrop: false);
                    var withBackdrop = Measure(mode, level, dpi, useBackdrop: true);
                    Console.WriteLine($"{dpi * 100,4:F0}% {name,-18} {withBackdrop.Size,-9} no-backdrop {plain.Ms:F3} ms ({plain.Ms / 16.667 * 100:F1}%)   backdrop {withBackdrop.Ms:F3} ms ({withBackdrop.Ms / 16.667 * 100:F1}% of a core)");
                }
            }

        }

        // Real GDI screen capture + reduction, as the overlay does it at 15 fps.
        private static void MeasureCapture()
        {
            foreach (var dpi in new[] { 1f, 1.5f, 2f })
            {
                var (w, h) = DropLayout.WindowSize(dpi, false, 0f);
                var region = BackdropCache.RegionFor(800, 700, w, h, dpi, new BackdropRegion(0, 0, 1920, 1080));
                var (tw, th) = BackdropCache.TextureSizeFor(region, dpi);
                var texture = new BackdropTexture(tw, th);
                using var source = new ScreenBackdropSource();
                var ok = source.TryCapture(region, texture);
                if (!ok)
                {
                    Console.WriteLine($"{dpi * 100,4:F0}% screen capture unavailable in this session");
                    continue;
                }

                for (var i = 0; i < 20; i++) source.TryCapture(region, texture);
                const int n = 200;
                var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < n; i++) source.TryCapture(region, texture);
                sw.Stop();
                var cpuMs = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds / n;
                var ms = sw.Elapsed.TotalMilliseconds / n;
                Console.WriteLine($"{dpi * 100,4:F0}% real screen capture {region.Width}x{region.Height}px -> {tw}x{th} texels: {ms:F3} ms wall, {cpuMs:F3} ms CPU per capture => {cpuMs * 15 / 1000 * 100:F2}% of a core at 15 fps (wall {ms * 15 / 1000 * 100:F2}%)");
            }
        }

        private static (double Ms, string Size) Measure(OverlayMode mode, float level, float dpi, bool useBackdrop)
        {
            var text = mode == OverlayMode.Notice ? TextMask.Create(OverlayText.For(OverlayNoticeKind.NothingDetected), (int)(15 * dpi)) : TextMask.Empty;
            var caption = mode == OverlayMode.Transcribing ? DropLayout.CreateCaption(OverlayText.Transcribing, dpi) : TextMask.Empty;
            var (w, h) = DropLayout.WindowSize(dpi, mode == OverlayMode.Notice, text.Width);
            var buffer = new uint[w * h];
            var renderer = new DropRenderer();
            var animator = new OverlayAnimator();
            var cache = new BackdropCache(useBackdrop ? new FuncBackdropSource((x, y) => FakeDesktops.Pixel(Desk.Wallpaper, (int)(x / dpi), (int)(y / dpi))) : null);
            var bounds = new BackdropRegion(0, 0, (int)(1920 * dpi), (int)(1080 * dpi));
            animator.Show(mode);
            var half = DropLayout.NoticeHalfWidth(text.Width, dpi);

            void Frame(int i, bool capture)
            {
                animator.SetLevel(level * (0.6f + 0.4f * MathF.Sin(i * 0.4f)));
                animator.Tick(1f / 60f);
                cache.Configure((int)(900 * dpi), (int)(800 * dpi), w, h, dpi, bounds);
                // The fake source is far slower than real capture, so only the cache smoothing is timed per frame.
                if (capture) cache.Advance(1f / 60f); else cache.Advance(0.0001f);
                renderer.Render(buffer, w, h, dpi, animator.Frame, text, half, caption, cache.Texture, cache.Availability);
            }

            for (var i = 0; i < 120; i++) Frame(i, true);
            const int frames = 1200;
            for (var i = 0; i < 600; i++) Frame(i, false);
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < frames; i++) Frame(i, false);
            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds / frames, $"{w}x{h}");
        }
    }
}
