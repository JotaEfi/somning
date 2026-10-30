using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Somning.Core.Settings;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Settings;
using Xunit;

namespace Somning.Integration.Tests.Settings;

/// <summary>Headless checks of the WPF settings view: the window is never shown, so no focus is taken.</summary>
public sealed class SettingsViewRenderTests
{
    private sealed class FakeProtector : ISettingsProtector
    {
        public string Protect(string plainText) => "enc:" + plainText;
        public string Unprotect(string cipherText) => cipherText.Replace("enc:", string.Empty, StringComparison.Ordinal);
    }

    private static readonly AudioDeviceInfo[] Devices =
    [
        new("dev-1", "Microfone (USB Audio)", true),
        new("dev-2", "Headset (Bluetooth)", false)
    ];

    private static T RunSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("STA action failed", error);
        return result!;
    }

    [Fact]
    public void BuildUpdated_UnchangedForm_PreservesSettingsAndStoredKey()
    {
        var original = new UserSettings
        {
            MicrophoneDeviceId = "dev-2", CloudProvider = "groq", EncryptedApiKey = "enc:secret",
            AutoPaste = false, PlaySoundCues = true, StartWithWindows = true, CustomVocabulary = "Somning, Whisper"
        };

        var updated = RunSta(() => new SettingsView(original, Devices, new FakeProtector()).BuildUpdated());

        Assert.Equal(original, updated);
    }

    [Fact]
    public void BuildUpdated_LocalDefaults_RoundTrip()
    {
        var original = new UserSettings();
        var updated = RunSta(() => new SettingsView(original, Devices, new FakeProtector()).BuildUpdated());
        Assert.Equal(original with { CloudProvider = "none" }, updated);
    }

    [Fact]
    public void RenderOffscreen_WritesPreviewPng()
    {
        var output = Environment.GetEnvironmentVariable("SOMNING_SETTINGS_PREVIEW")
            ?? Path.Combine(Path.GetTempPath(), "somning-settings-preview.png");
        var settings = new UserSettings { MicrophoneDeviceId = "dev-1", CloudProvider = "openai", EncryptedApiKey = "enc:x", PlaySoundCues = true, AutoPaste = true };

        var bytes = RunSta(() => RenderOverWallpaper(settings, acrylic: Environment.GetEnvironmentVariable("SOMNING_SETTINGS_PREVIEW_SOLID") is null));

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        Assert.True(bytes.Length > 10_000);
    }

    [Fact]
    public void Layout_IsCompact_NoScrollNeeded()
    {
        var size = RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
            var root = (FrameworkElement)view.Content;
            view.Content = null;
            var host = new System.Windows.Controls.Border { Child = root };
            TextElementSetup(host, view);
            host.Measure(new Size(view.Width, double.PositiveInfinity));
            return host.DesiredSize;
        });
        Assert.InRange(size.Height, 300, 720);
    }

    /// <summary>Renders the settings content over a synthetic wallpaper; in acrylic mode the area behind the glass is blurred to simulate the backdrop.</summary>
    private static byte[] RenderOverWallpaper(UserSettings settings, bool acrylic)
    {
        const double pad = 70;
        var view = new SettingsView(settings, Devices, new FakeProtector());
        view.UseBackdropFill(acrylic);
        var root = (FrameworkElement)view.Content;
        view.Content = null;
        var content = new System.Windows.Controls.Border { Child = root, Width = view.Width };
        TextElementSetup(content, view);
        content.Measure(new Size(view.Width, double.PositiveInfinity));
        var width = view.Width + pad * 2;
        var height = Math.Ceiling(content.DesiredSize.Height) + pad * 2;

        var stage = new System.Windows.Controls.Grid { Width = width, Height = height };
        stage.Children.Add(Wallpaper(width, height));
        if (acrylic)
        {
            var margin = root.Margin;
            var glass = new Rect(pad + margin.Left, pad + margin.Top, view.Width - margin.Left - margin.Right, content.DesiredSize.Height - margin.Top - margin.Bottom);
            var blurred = Wallpaper(width, height);
            blurred.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 40 };
            var blurHost = new System.Windows.Controls.Grid { Clip = new RectangleGeometry(glass, 22, 22) };
            blurHost.Children.Add(blurred);
            stage.Children.Add(blurHost);
        }
        content.HorizontalAlignment = HorizontalAlignment.Left;
        content.VerticalAlignment = VerticalAlignment.Top;
        content.Margin = new Thickness(pad);
        stage.Children.Add(content);

        stage.Measure(new Size(width, height));
        stage.Arrange(new Rect(0, 0, width, height));
        stage.UpdateLayout();
        view.UseBackdropFill(acrylic); // geometry clips now that the glass has a size
        stage.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Colourful synthetic desktop: dusk gradient, soft blobs and a few "window" slabs, so translucency is visible.</summary>
    private static System.Windows.Controls.Canvas Wallpaper(double width, double height)
    {
        var canvas = new System.Windows.Controls.Canvas
        {
            Width = width, Height = height,
            Background = new LinearGradientBrush(Color.FromRgb(0x2B, 0x4C, 0x7E), Color.FromRgb(0xC7, 0x6B, 0x4A), 45)
        };
        void Blob(double x, double y, double r, Color c)
        {
            var e = new System.Windows.Shapes.Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B))
            };
            System.Windows.Controls.Canvas.SetLeft(e, x - r);
            System.Windows.Controls.Canvas.SetTop(e, y - r);
            canvas.Children.Add(e);
        }
        Blob(width * 0.2, height * 0.25, 220, Color.FromRgb(0xF2, 0xC1, 0x4E));
        Blob(width * 0.85, height * 0.4, 260, Color.FromRgb(0x4E, 0xD8, 0xC0));
        Blob(width * 0.5, height * 0.9, 240, Color.FromRgb(0xE0, 0x4E, 0x8A));
        for (var i = 0; i < 6; i++)
        {
            var slab = new System.Windows.Shapes.Rectangle { Width = 150, Height = 90, RadiusX = 8, RadiusY = 8, Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0xF4, 0xF4, 0xF0)) };
            System.Windows.Controls.Canvas.SetLeft(slab, 30 + i * 105);
            System.Windows.Controls.Canvas.SetTop(slab, 40 + (i % 3) * 220);
            canvas.Children.Add(slab);
        }
        return canvas;
    }

    private static void TextElementSetup(FrameworkElement host, Window view)
    {
        host.Resources.MergedDictionaries.Add(view.Resources);
        System.Windows.Documents.TextElement.SetForeground(host, view.Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(host, view.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(host, view.FontSize);
    }
}