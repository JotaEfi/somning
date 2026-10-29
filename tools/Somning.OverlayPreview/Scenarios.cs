using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;

namespace Somning.OverlayPreview;

/// <summary>Drives the real animator + renderer through the contest scenarios and writes composited PNGs.</summary>
internal static class Scenarios
{
    private const float Dt = 1f / 60f;
    private static readonly OverlayNoticeKind Notice = OverlayNoticeKind.NothingDetected;

    private enum Backdrop { Dark, Light }

    /// <summary>Simulated raw voice level (tracker RMS) for a scenario clock.</summary>
    private static float Voice(float amplitude, float t) =>
        amplitude * (0.55f + 0.30f * MathF.Sin(t * 17f) + 0.15f * MathF.Sin(t * 41f + 1f));

    private sealed class Run
    {
        public readonly OverlayAnimator Animator = new();
        public float Clock;
        public OverlayMode Mode = OverlayMode.Hidden;
        public OverlayNoticeKind? NoticeKind;

        public void Show(OverlayMode mode, OverlayNoticeKind? notice = null)
        {
            Mode = mode;
            NoticeKind = notice;
            Animator.Show(mode);
        }

        public void Advance(float seconds, Func<float, float>? level = null)
        {
            var steps = (int)MathF.Round(seconds / Dt);
            for (var i = 0; i < steps; i++)
            {
                Clock += Dt;
                Animator.SetLevel(level?.Invoke(Clock) ?? 0f);
                Animator.Tick(Dt);
            }
        }
    }

    public static void WriteAll(string dir)
    {
        var scenes = new (string Name, Func<Run> Build)[]
        {
            ("01-appear-t0.10s", () => { var r = new Run(); r.Show(OverlayMode.Listening); r.Advance(0.10f); return r; }),
            ("02-appear-t0.25s", () => { var r = new Run(); r.Show(OverlayMode.Listening); r.Advance(0.25f); return r; }),
            ("03-listening-silence", () => { var r = new Run(); r.Show(OverlayMode.Listening); r.Advance(2.0f); return r; }),
            ("04-listening-voice-medium", () => Listening(0.05f, 0.05f)),
            ("05-listening-voice-loud", () => Listening(0.19f, 0.19f)),
            ("06-transcribing", () => { var r = Listening(0.08f, 0.08f); r.Show(OverlayMode.Transcribing); r.Advance(1.3f); return r; }),
            ("07-error", () => { var r = Listening(0.06f, 0.06f); r.Show(OverlayMode.Notice, Notice); r.Advance(1.1f); return r; }),
            ("08-disappear-t0.10s", () => { var r = Listening(0.07f, 0.07f); r.Animator.Hide(); r.Advance(0.10f); return r; })
        };

        foreach (var (name, build) in scenes)
        {
            var run = build();
            foreach (var dpi in new[] { 1f, 3f })
            {
                foreach (var backdrop in new[] { Backdrop.Dark, Backdrop.Light })
                {
                    var (w, h, rgb) = Compose(run, dpi, backdrop);
                    var suffix = $"{(backdrop == Backdrop.Dark ? "dark" : "light")}{(dpi > 1f ? "-3x" : string.Empty)}";
                    PngWriter.WriteRgb(Path.Combine(dir, $"{name}-{suffix}.png"), w, h, rgb);
                }
            }
        }

        ContactSheet(dir, Backdrop.Dark, "contact-sheet.png");
        ContactSheet(dir, Backdrop.Light, "contact-sheet-light.png");
    }

    private static Run Listening(float peakLevel, float steadyLevel)
    {
        var r = new Run();
        r.Show(OverlayMode.Listening);
        r.Advance(0.5f);
        r.Advance(1.6f, t => Voice(peakLevel, t));
        _ = steadyLevel;
        return r;
    }

    private static (int W, int H, byte[] Rgb) Compose(Run run, float dpi, Backdrop backdrop)
    {
        var text = run.NoticeKind is { } kind ? TextMask.Create(OverlayText.For(kind), (int)Math.Round(15 * dpi)) : TextMask.Empty;
        var (w, h) = DropLayout.WindowSize(dpi, run.Animator.Frame.Morph > 0.01f, text.Width);
        var pixels = new uint[w * h];
        var caption = run.Mode == OverlayMode.Transcribing
            ? TextMask.Create(OverlayText.Transcribing, (int)Math.Round(13 * dpi))
            : TextMask.Empty;
        new DropRenderer { NativeDpiLimit = float.MaxValue }.Render(pixels, w, h, dpi, run.Animator.Frame, text, DropLayout.NoticeHalfWidth(text.Width, dpi), caption);
        return (w, h, CompositeOver(pixels, w, h, backdrop));
    }

    private static byte[] CompositeOver(uint[] pixels, int w, int h, Backdrop backdrop)
    {
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var (br, bg, bb) = Background(backdrop, x, y, w, h);
                var p = pixels[y * w + x];
                var a = (p >> 24) / 255f;
                var i = (y * w + x) * 3;
                rgb[i] = Blend((p >> 16) & 0xFF, br, a);
                rgb[i + 1] = Blend((p >> 8) & 0xFF, bg, a);
                rgb[i + 2] = Blend(p & 0xFF, bb, a);
            }
        }

        return rgb;
    }

    // Premultiplied source over opaque backdrop.
    private static byte Blend(uint premultiplied, float backdrop, float alpha) =>
        (byte)Math.Clamp(premultiplied + backdrop * (1f - alpha) + 0.5f, 0f, 255f);

    private static (float R, float G, float B) Background(Backdrop backdrop, int x, int y, int w, int h)
    {
        if (backdrop == Backdrop.Light) return (0xF5, 0xF5, 0xF5);
        // #2B2F36 with a soft diagonal gradient, like a dark wallpaper.
        var t = (x / (float)w + y / (float)h) * 0.5f;
        var k = 0.82f + 0.36f * t;
        return (0x2B * k, 0x2F * k, 0x36 * k);
    }

    private static void ContactSheet(string dir, Backdrop backdrop, string fileName)
    {
        // appear, speak with peaks, fall silent, transcribe, disappear: one continuous simulation.
        float[] captureTimes = [0.07f, 0.12f, 0.25f, 0.55f, 1.0f, 1.35f, 1.75f, 2.3f, 3.0f, 3.5f, 4.17f, 4.30f];
        Func<float, float> level = t => t switch
        {
            < 0.6f => 0f,
            < 2.2f => Voice(0.04f + 0.15f * MathF.Abs(MathF.Sin(t * 2.1f)), t),
            < 2.6f => Voice(0.05f, t) * (1f - (t - 2.2f) / 0.4f),
            _ => 0f
        };

        var run = new Run();
        run.Show(OverlayMode.Listening);
        var frames = new List<(int W, int H, byte[] Rgb)>();
        var next = 0;
        var transcribing = false;
        var hidden = false;
        for (var step = 0; step < 400 && next < captureTimes.Length; step++)
        {
            run.Clock += Dt;
            if (!transcribing && run.Clock >= 3.2f) { run.Show(OverlayMode.Transcribing); transcribing = true; }
            if (!hidden && run.Clock >= 4.2f) { run.Animator.Hide(); hidden = true; }
            run.Animator.SetLevel(level(run.Clock));
            run.Animator.Tick(Dt);
            if (run.Clock + 0.5f * Dt >= captureTimes[next])
            {
                frames.Add(Compose(run, 1f, backdrop));
                next++;
            }
        }

        const int columns = 6;
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

        PngWriter.WriteRgb(Path.Combine(dir, fileName), sheetW, sheetH, sheet);
    }
}
