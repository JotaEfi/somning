using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

/// <summary>
/// Contract every selectable drop style must honor: the dark, voice-reactive band in the middle of the bubble while
/// listening, and a transcribing bubble that is clearly the same drop (same silhouette, still dark in the middle).
/// </summary>
public sealed class DropStyleContractTests
{
    private const float Step = 1f / 60f;

    public static IEnumerable<object[]> StylesToHold() =>
        DropStyles.All.Where(style => style.Id != DropStyles.Classic).Select(style => new object[] { style.Id });

    private static DropFrame Settled(OverlayMode mode, float level, float seconds = 2f, float tone = 0.5f)
    {
        var animator = new OverlayAnimator();
        animator.Show(mode);
        for (var t = 0f; t < seconds; t += Step)
        {
            animator.SetLevel(mode == OverlayMode.Listening ? level : 0f);
            animator.SetTone(tone);
            animator.Tick(Step);
        }

        return animator.Frame;
    }

    /// <summary>A bright, flat desktop behind the drop: the dark band must read against it.</summary>
    private static BackdropTexture LightDesktop(int width, int height)
    {
        var texture = new BackdropTexture(8, 8) { OriginX = -40f, OriginY = -40f, PixelsPerTexelX = (width + 80f) / 8f, PixelsPerTexelY = (height + 80f) / 8f };
        texture.Fill(0.85f, 0.85f, 0.88f);
        return texture;
    }

    private static uint[] Render(string style, in DropFrame frame, out int w, out int h)
    {
        (w, h) = DropLayout.WindowSize(1f, false, 0f);
        var buffer = new uint[w * h];
        new DropRenderer { Style = style }.Render(buffer, w, h, 1f, in frame, null, 0f, null, LightDesktop(w, h), 1f);
        return buffer;
    }

    private static float Luma(uint p)
    {
        var a = (p >> 24) / 255f;
        if (a < 0.02f) return 1f;
        var r = ((p >> 16) & 255) / 255f / a;
        var g = ((p >> 8) & 255) / 255f / a;
        var b = (p & 255) / 255f / a;
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    /// <summary>Share of the opaque pixels in the central band of the drop that are dark.</summary>
    internal static (float DarkShare, float MeanLuma) CenterBand(uint[] buffer, int w, int h)
    {
        int dark = 0, count = 0;
        double sum = 0;
        var cx = w / 2;
        var cy = h / 2;
        for (var y = cy - 14; y <= cy + 14; y++)
        {
            for (var x = cx - 20; x <= cx + 20; x++)
            {
                var p = buffer[y * w + x];
                if ((p >> 24) < 90) continue;
                var l = Luma(p);
                sum += l;
                count++;
                if (l < 0.22f) dark++;
            }
        }

        return count == 0 ? (0f, 1f) : (dark / (float)count, (float)(sum / count));
    }

    private static float SilhouetteOverlap(uint[] a, uint[] b)
    {
        int both = 0, either = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var ia = (a[i] >> 24) > 60;
            var ib = (b[i] >> 24) > 60;
            if (ia || ib) either++;
            if (ia && ib) both++;
        }

        return either == 0 ? 1f : both / (float)either;
    }

    // Thresholds calibrated on the Classic drop over a light desktop (listening, voice 0.12): dark share 0.28 and mean
    // luma 0.36 in the center band, against 0.07 / 0.43 in silence. New styles must be at least that dark and reactive.
    private const float MinDarkShare = 0.20f;
    private const float MaxMeanLuma = 0.45f;
    private const float MinReaction = 0.04f;

    [Fact]
    public void Classic_is_the_reference_the_thresholds_were_calibrated_on()
    {
        var listening = Render(DropStyles.Classic, Settled(OverlayMode.Listening, 0.12f), out var w, out var h);
        var (dark, mean) = CenterBand(listening, w, h);
        Assert.True(dark >= MinDarkShare && mean <= MaxMeanLuma, $"classic dark={dark:F2} mean={mean:F2}");
    }

    [Theory]
    [MemberData(nameof(StylesToHold))]
    public void Listening_drop_has_the_dark_center_band(string style)
    {
        var buffer = Render(style, Settled(OverlayMode.Listening, 0.12f), out var w, out var h);
        var (dark, mean) = CenterBand(buffer, w, h);
        Assert.True(dark >= MinDarkShare, $"{style}: dark share in the center band {dark:F2} < {MinDarkShare}");
        Assert.True(mean <= MaxMeanLuma, $"{style}: mean luma in the center band {mean:F2} > {MaxMeanLuma}");
    }

    [Theory]
    [MemberData(nameof(StylesToHold))]
    public void The_dark_center_band_reacts_to_the_voice(string style)
    {
        var quiet = Render(style, Settled(OverlayMode.Listening, 0f), out var w, out var h);
        var loud = Render(style, Settled(OverlayMode.Listening, 0.18f), out _, out _);
        var (_, quietMean) = CenterBand(quiet, w, h);
        var (_, loudMean) = CenterBand(loud, w, h);
        Assert.True(Math.Abs(loudMean - quietMean) >= MinReaction, $"{style}: center luma quiet {quietMean:F2} vs loud {loudMean:F2}");
    }

    [Theory]
    [MemberData(nameof(StylesToHold))]
    public void Transcribing_drop_is_the_same_bubble_as_the_listening_one(string style)
    {
        var quiet = Render(style, Settled(OverlayMode.Listening, 0f), out var w, out var h);
        var transcribing = Render(style, Settled(OverlayMode.Transcribing, 0f), out _, out _);
        var overlap = SilhouetteOverlap(quiet, transcribing);
        var (dark, mean) = CenterBand(transcribing, w, h);
        Assert.True(overlap >= 0.80f, $"{style}: silhouette overlap listening/transcribing {overlap:F2}");
        Assert.True(mean <= 0.55f, $"{style}: transcribing center luma {mean:F2} is too bright to read as the same dark bubble");
        Assert.True(dark >= 0.10f, $"{style}: transcribing keeps no dark center band (share {dark:F2})");
    }

    [Theory]
    [MemberData(nameof(StylesToHold))]
    public void Pixels_are_valid_premultiplied_colors_in_every_mode(string style)
    {
        foreach (var mode in new[] { OverlayMode.Listening, OverlayMode.Transcribing })
        {
            var buffer = Render(style, Settled(mode, 0.12f), out _, out _);
            Assert.All(buffer, p =>
            {
                var a = p >> 24;
                Assert.True(((p >> 16) & 255) <= a && ((p >> 8) & 255) <= a && (p & 255) <= a, $"{style}/{mode}: channel above alpha");
            });
        }
    }
}
