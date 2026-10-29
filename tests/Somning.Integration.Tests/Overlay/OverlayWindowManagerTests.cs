using System.Runtime.InteropServices;
using Somning.Core.Overlay;
using Somning.Platform.Windows.Overlay;
using Xunit;

namespace Somning.Integration.Tests.Overlay;

public sealed class OverlayWindowManagerTests
{
    private const int GwlExStyle = -20;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExTransparent = 0x00000020;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [Fact]
    public void OverlayWindow_HasExpectedNonIntrusiveStyles()
    {
        using var overlay = new OverlayWindowManager();
        Assert.NotEqual(0, overlay.Hwnd);

        var exStyle = (uint)GetWindowLongPtr(overlay.Hwnd, GwlExStyle);

        // OVL-02: Window styles assert non-intrusive properties
        Assert.True((exStyle & WsExTopmost) != 0, "Overlay must have WS_EX_TOPMOST");
        Assert.True((exStyle & WsExToolWindow) != 0, "Overlay must have WS_EX_TOOLWINDOW");
        Assert.True((exStyle & WsExTransparent) != 0, "Overlay must have WS_EX_TRANSPARENT");
        Assert.True((exStyle & WsExNoActivate) != 0, "Overlay must have WS_EX_NOACTIVATE");
        Assert.True((exStyle & WsExLayered) != 0, "Overlay must have WS_EX_LAYERED");
    }

    [Fact]
    public async Task ShowingOverlay_DoesNotChangeForegroundWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var overlay = new OverlayWindowManager();
        var fgBefore = GetForegroundWindow();

        overlay.ShowListening([0.2f, 0.4f, 0.8f, 0.4f, 0.2f]);
        await Task.Delay(100, ct);

        var fgDuringListening = GetForegroundWindow();
        Assert.Equal(fgBefore, fgDuringListening);

        overlay.ShowTranscribing();
        await Task.Delay(100, ct);

        var fgDuringTranscribing = GetForegroundWindow();
        Assert.Equal(fgBefore, fgDuringTranscribing);

        overlay.ShowNotice(OverlayNoticeKind.MicMissing);
        await Task.Delay(100, ct);

        var fgDuringNotice = GetForegroundWindow();
        Assert.Equal(fgBefore, fgDuringNotice);

        overlay.Hide();
        await Task.Delay(50, ct);

        var fgAfter = GetForegroundWindow();
        Assert.Equal(fgBefore, fgAfter);
    }

    [Fact]
    public void OverlayStates_TransitionAndFormatPtBrCorrectly()
    {
        using var overlay = new OverlayWindowManager();
        Assert.False(overlay.IsVisible);
        Assert.Equal(OverlayMode.Hidden, overlay.CurrentModel.Mode);

        // 1. Listening
        float[] bars = [0.15f, 0.5f, 1.0f, 0.5f, 0.15f];
        overlay.ShowListening(bars);
        Assert.True(overlay.IsVisible);
        Assert.Equal(OverlayMode.Listening, overlay.CurrentModel.Mode);
        Assert.Equal("Ouvindo...", overlay.CurrentModel.Text);
        Assert.Equal(5, overlay.CurrentModel.Bars.Length);

        // 2. Transcribing
        overlay.ShowTranscribing();
        Assert.True(overlay.IsVisible);
        Assert.Equal(OverlayMode.Transcribing, overlay.CurrentModel.Mode);
        Assert.Equal("Transcrevendo...", overlay.CurrentModel.Text);

        // 3. Notice states (pt-BR - APP-05)
        overlay.ShowNotice(OverlayNoticeKind.MicMissing);
        Assert.True(overlay.IsVisible);
        Assert.Equal(OverlayMode.Notice, overlay.CurrentModel.Mode);
        Assert.Equal("Microfone não encontrado", overlay.CurrentModel.Text);

        overlay.ShowNotice(OverlayNoticeKind.SilenceOnly);
        Assert.Equal("Apenas silêncio detectado", overlay.CurrentModel.Text);

        // 4. Hide
        overlay.Hide();
        Assert.False(overlay.IsVisible);
        Assert.Equal(OverlayMode.Hidden, overlay.CurrentModel.Mode);
    }

    [Theory]
    [InlineData(1.0f, 280, 176)]
    [InlineData(1.5f, 420, 264)]
    [InlineData(2.0f, 560, 352)]
    public void PixelBufferRendering_At100150And200Dpi_MatchesOrbGeometryAndTransparency(float scale, int width, int height)
    {
        var layout = OrbLayout.Scaled(scale);
        Assert.Equal(width, layout.Width);
        Assert.Equal(height, layout.Height);

        var buffer = new uint[width * height];
        var model = OverlayViewModel.CreateListening([0.2f, 0.5f, 0.9f, 0.5f, 0.2f]);

        OverlayWindowManager.RenderToBuffer(buffer, width, height, scale, model);

        // Corners stay fully transparent; the orb sits in the horizontal center of the upper zone.
        Assert.Equal(0u, buffer[0]);
        Assert.Equal(0u, buffer[width - 1]);
        Assert.Equal(0u, buffer[(height - 1) * width]);
        Assert.Equal(0u, buffer[(height - 1) * width + width - 1]);

        var center = buffer[(int)layout.CenterY * width + width / 2];
        Assert.True((center >> 24) > 120, "orb core must be visible glass");

        // Premultiplied BGRA invariant for UpdateLayeredWindow.
        foreach (var pixel in buffer)
        {
            var alpha = pixel >> 24;
            Assert.True(((pixel >> 16) & 0xFF) <= alpha && ((pixel >> 8) & 0xFF) <= alpha && (pixel & 0xFF) <= alpha);
        }
    }
}
