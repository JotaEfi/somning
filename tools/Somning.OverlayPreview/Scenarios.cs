using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;

namespace Somning.OverlayPreview;

/// <summary>Drives the real animator + backdrop cache + renderer through scenarios over fake desktops and writes PNGs.</summary>
internal static class Scenarios
{
    private const float Dt = 1f / 60f;
    private static readonly OverlayNoticeKind Notice = OverlayNoticeKind.NothingDetected;

    /// <summary>Simulated raw voice level (tracker RMS) for a scenario clock.</summary>
    private static float Voice(float amplitude, float t) =>
        amplitude * (0.55f + 0.30f * MathF.Sin(t * 17f) + 0.15f * MathF.Sin(t * 41f + 1f));

    /// <summary>Speech-like level: syllable bursts (about 4 per second) with gaps.</summary>
    private static float Speech(float amplitude, float t)
    {
        var syllable = MathF.Max(0f, MathF.Sin(t * 8.5f)) * (0.6f + 0.4f * MathF.Sin(t * 2.3f));
        return amplitude * (0.15f + 0.85f * syllable) * (0.8f + 0.2f * MathF.Sin(t * 33f));
    }

    private sealed class Run
    {
        public readonly OverlayAnimator Animator = new();
        public readonly BackdropCache Cache;
        public readonly float Dpi;
        public float Clock;
        public OverlayMode Mode = OverlayMode.Hidden;
        public OverlayNoticeKind? NoticeKind;
        public int WindowX, WindowY;

        public Run(Desk desk, float dpi, bool useBackdrop = true)
        {
            Dpi = dpi;
            Cache = new BackdropCache(useBackdrop ? new FuncBackdropSource((x, y) => FakeDesktops.Pixel(desk, (int)(x / dpi), (int)(y / dpi))) : null);
        }

        public (TextMask Text, int W, int H) Layout()
        {
            var text = NoticeKind is { } kind ? TextMask.Create(OverlayText.For(kind), (int)Math.Round(15 * Dpi)) : TextMask.Empty;
            var (w, h) = DropLayout.WindowSize(Dpi, Animator.Frame.Morph > 0.01f, text.Width);
            WindowX = (int)(960 * Dpi) - w / 2;
            WindowY = (int)((FakeDesktops.WorkBottom - 14) * Dpi) - h;
            return (text, w, h);
        }

        public void Show(OverlayMode mode, OverlayNoticeKind? notice = null)
        {
            Mode = mode;
            NoticeKind = notice;
            Animator.Show(mode);
        }

        public void Advance(float seconds, Func<float, float>? level = null)
        {
            var steps = (int)MathF.Round(seconds / Dt);
            for (var i = 0; i < steps; i++) Step(level);
        }

        public void Step(Func<float, float>? level)
        {
            Clock += Dt;
            Animator.SetLevel(level?.Invoke(Clock) ?? 0f);
            Animator.Tick(Dt);
            var (_, w, h) = Layout();
            Cache.Configure(WindowX, WindowY, w, h, Dpi, new BackdropRegion(0, 0, (int)(FakeDesktops.ScreenWidth * Dpi), (int)(FakeDesktops.ScreenHeight * Dpi)));
            Cache.Advance(Dt);
        }
    }

    public static void WriteAll(string dir)
    {
        foreach (var desk in Enum.GetValues<Desk>())
        {
            var name = desk.ToString().ToLowerInvariant();
            var states = States();
            var sheet = new List<(int W, int H, byte[] Rgb)>();
            foreach (var (label, build) in states)
            {
                var run = build(desk, 1f, true);
                var frame = Compose(run, desk);
                PngWriter.WriteRgb(Path.Combine(dir, $"{name}-{label}.png"), frame.W, frame.H, frame.Rgb);
                sheet.Add(frame);
            }

            Sheet(Path.Combine(dir, $"sheet-{name}.png"), sheet, 4);
            Timeline(dir, desk);
        }

        // Hi-DPI check and the reflection toggled off, for comparison.
        foreach (var desk in new[] { Desk.Wallpaper, Desk.Document, Desk.Clock, Desk.Code })
        {
            var run = States()[4].Build(desk, 3f, true);
            var f = Compose(run, desk);
            PngWriter.WriteRgb(Path.Combine(dir, $"{desk.ToString().ToLowerInvariant()}-05-voice-loud-3x.png"), f.W, f.H, f.Rgb);
            var transcribing = States()[5].Build(desk, 3f, true);
            var t = Compose(transcribing, desk);
            PngWriter.WriteRgb(Path.Combine(dir, $"{desk.ToString().ToLowerInvariant()}-06-transcribing-3x.png"), t.W, t.H, t.Rgb);
        }

        var plain = States()[4].Build(Desk.Wallpaper, 1f, false);
        var p = Compose(plain, Desk.Wallpaper);
        PngWriter.WriteRgb(Path.Combine(dir, "wallpaper-05-voice-loud-NO-BACKDROP.png"), p.W, p.H, p.Rgb);
    }

    private static (string Label, Func<Desk, float, bool, Run> Build)[] States()
    {
        Run Make(Desk d, float dpi, bool b) => new(d, dpi, b);
        return
        [
            ("01-appear-t0.10s", (d, dpi, b) => { var r = Make(d, dpi, b); r.Show(OverlayMode.Listening); r.Advance(0.10f); return r; }),
            ("02-appear-t0.25s", (d, dpi, b) => { var r = Make(d, dpi, b); r.Show(OverlayMode.Listening); r.Advance(0.25f); return r; }),
            ("03-silence", (d, dpi, b) => { var r = Make(d, dpi, b); r.Show(OverlayMode.Listening); r.Advance(2.2f); return r; }),
            ("04-voice-medium", (d, dpi, b) => Listening(Make(d, dpi, b), 0.06f)),
            ("05-voice-loud", (d, dpi, b) => Listening(Make(d, dpi, b), 0.20f)),
            ("06-transcribing", (d, dpi, b) => { var r = Listening(Make(d, dpi, b), 0.08f); r.Show(OverlayMode.Transcribing); r.Advance(1.4f); return r; }),
            ("07-error", (d, dpi, b) => { var r = Listening(Make(d, dpi, b), 0.06f); r.Show(OverlayMode.Notice, Notice); r.Advance(1.2f); return r; }),
            ("08-dissolve-t0.12s", (d, dpi, b) => { var r = Listening(Make(d, dpi, b), 0.07f); r.Animator.Hide(); r.Advance(0.12f); return r; })
        ];
    }

    private static Run Listening(Run r, float peakLevel)
    {
        r.Show(OverlayMode.Listening);
        r.Advance(0.5f);
        r.Advance(1.7f, t => Voice(peakLevel, t));
        return r;
    }

    private static (int W, int H, byte[] Rgb) Compose(Run run, Desk desk)
    {
        var (text, w, h) = run.Layout();
        var pixels = new uint[w * h];
        var caption = run.Mode == OverlayMode.Transcribing ? DropLayout.CreateCaption(OverlayText.Transcribing, run.Dpi) : TextMask.Empty;
        var renderer = new DropRenderer();
        renderer.Render(pixels, w, h, run.Dpi, run.Animator.Frame, text, DropLayout.NoticeHalfWidth(text.Width, run.Dpi), caption, run.Cache.Texture, run.Cache.Availability);

        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var bg = FakeDesktops.Pixel(desk, (int)((run.WindowX + x) / run.Dpi), (int)((run.WindowY + y) / run.Dpi));
                var p = pixels[y * w + x];
                var a = (p >> 24) / 255f;
                var i = (y * w + x) * 3;
                rgb[i] = Blend((p >> 16) & 0xFF, (bg >> 16) & 0xFF, a);
                rgb[i + 1] = Blend((p >> 8) & 0xFF, (bg >> 8) & 0xFF, a);
                rgb[i + 2] = Blend(p & 0xFF, bg & 0xFF, a);
            }
        }

        return (w, h, rgb);
    }

    // Premultiplied source over opaque backdrop.
    private static byte Blend(uint premultiplied, uint backdrop, float alpha) =>
        (byte)Math.Clamp(premultiplied + backdrop * (1f - alpha) + 0.5f, 0f, 255f);

    private static void Timeline(string dir, Desk desk)
    {
        // appear, speak in syllables, fall silent, transcribe, disappear: one continuous simulation.
        float[] captureTimes = [0.07f, 0.12f, 0.25f, 0.55f, 0.9f, 1.15f, 1.40f, 1.65f, 2.0f, 2.45f, 3.0f, 3.5f, 4.17f, 4.30f, 4.5f, 4.55f];
        Func<float, float> level = t => t switch
        {
            < 0.6f => 0f,
            < 2.2f => Speech(0.20f, t),
            < 2.6f => Speech(0.08f, t) * (1f - (t - 2.2f) / 0.4f),
            _ => 0f
        };

        var run = new Run(desk, 1f);
        run.Show(OverlayMode.Listening);
        var frames = new List<(int W, int H, byte[] Rgb)>();
        var next = 0;
        var transcribing = false;
        var hidden = false;
        for (var step = 0; step < 600 && next < captureTimes.Length; step++)
        {
            if (!transcribing && run.Clock >= 3.2f) { run.Show(OverlayMode.Transcribing); transcribing = true; }
            if (!hidden && run.Clock >= 4.2f) { run.Animator.Hide(); hidden = true; }
            run.Step(level);
            if (run.Clock + 0.5f * Dt >= captureTimes[next])
            {
                frames.Add(Compose(run, desk));
                next++;
            }
        }

        Sheet(Path.Combine(dir, $"timeline-{desk.ToString().ToLowerInvariant()}.png"), frames, 4);
    }

    private static void Sheet(string path, List<(int W, int H, byte[] Rgb)> frames, int columns)
    {
        var rows = (frames.Count + columns - 1) / columns;
        var cellW = frames.Max(f => f.W);
        var cellH = frames.Max(f => f.H);
        var sheetW = cellW * columns;
        var sheetH = cellH * rows;
        var sheet = new byte[sheetW * sheetH * 3];
        for (var i = 0; i < frames.Count; i++)
        {
            var (fw, fh, rgb) = frames[i];
            var ox = (i % columns) * cellW + (cellW - fw) / 2;
            var oy = (i / columns) * cellH + (cellH - fh) / 2;
            for (var y = 0; y < fh; y++)
                Buffer.BlockCopy(rgb, y * fw * 3, sheet, ((oy + y) * sheetW + ox) * 3, fw * 3);
        }

        PngWriter.WriteRgb(path, sheetW, sheetH, sheet);
    }
}
