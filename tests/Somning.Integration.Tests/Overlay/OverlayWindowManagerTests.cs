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
    [InlineData(1.0f, 220, 44)]
    [InlineData(1.5f, 330, 66)]
    public void PixelBufferRendering_At100And150Dpi_MatchesPillGeometryAndTransparency(float scale, int width, int height)
    {
        var buffer = new uint[width * height];
        var model = OverlayViewModel.CreateListening([0.2f, 0.5f, 0.9f, 0.5f, 0.2f]);

        OverlayWindowManager.RenderToBuffer(buffer, width, height, scale, model);

        // 1. Corners must be transparent (alpha == 0)
        Assert.Equal(0u, buffer[0]);                                        // Top-left corner
        Assert.Equal(0u, buffer[width - 1]);                                // Top-right corner
        Assert.Equal(0u, buffer[(height - 1) * width]);                     // Bottom-left corner
        Assert.Equal(0u, buffer[(height - 1) * width + width - 1]);         // Bottom-right corner

        // 2. Center pixel must be within pill background (non-zero alpha)
        var centerIdx = (height / 2) * width + (width / 2);
        var centerPixel = buffer[centerIdx];
        var alpha = (byte)(centerPixel >> 24);
        Assert.True(alpha > 200, $"Center pixel alpha must be > 200, got {alpha}");

        // 3. Status indicator dot region must contain red pixels (#EF4444)
        int dotCenterX = (int)Math.Round(20 * scale);
        int dotCenterY = height / 2;
        var dotPixel = buffer[dotCenterY * width + dotCenterX];
        var dotAlpha = (byte)(dotPixel >> 24);
        var dotRed = (byte)((dotPixel >> 16) & 0xFF);
        Assert.Equal(0xFF, dotAlpha);
        Assert.Equal(239, dotRed); // #EF is 239

        // 4. Waveform bars region must have non-zero pixels
        int barY = height / 2;
        int barSampleX = width - (int)Math.Round(30 * scale);
        var barPixel = buffer[barY * width + barSampleX];
        Assert.NotEqual(0u, barPixel);
    }
}
