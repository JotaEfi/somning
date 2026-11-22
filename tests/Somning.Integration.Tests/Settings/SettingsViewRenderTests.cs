using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Somning.Core.Overlay;
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
    public void ThreeCustomBindings_RoundTripWithoutChangingOtherSettings()
    {
        var original = new UserSettings
        {
            Hotkey = new(KeyModifiers.Control | KeyModifiers.Alt, "D"),
            CancelHotkey = new(KeyModifiers.Alt, "F8"),
            ConfirmHotkey = new(KeyModifiers.Control | KeyModifiers.Shift, "Return"),
            CloudProvider = "none", CustomVocabulary = "Somning"
        };
        var updated = RunSta(() => new SettingsView(original, Devices, new FakeProtector()).BuildUpdated());
        Assert.Equal(original, updated);
    }

    [Fact]
    public void DuplicateBindings_CannotBeBuiltForSaving()
    {
        var rejected = RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
            ((Button)view.FindName("CancelHotkeyButton")).Tag = HotkeyBinding.Default;
            return Assert.Throws<InvalidOperationException>(() => view.BuildUpdated()).Message;
        });
        Assert.False(string.IsNullOrWhiteSpace(rejected));
    }

    [Fact]
    public void Header_UsesCapitalSomningAndCorrectPortugueseTitle()
    {
        var result = RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
            return (view.Title, ((TextBlock)view.FindName("BrandName")).Text);
        });
        Assert.Equal("Somning — Configurações", result.Title);
        Assert.Equal("Somning", result.Text);
    }

    [Fact]
    public void ShortcutRecorder_OwnsHeldSpaceAndEnterUntilMatchingKeyUp()
    {
        RunSta(() =>
        {
            foreach (var key in new[] { Key.Space, Key.Return })
            {
                var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
                var button = (Button)view.FindName("ActivateHotkeyButton");
                var saved = 0;
                view.Saved += _ => saved++;
                view.BeginHotkeyCapture(button, requestFocus: false);
                Assert.True(view.ProcessHotkeyInput(key, Key.None, ModifierKeys.Alt, isDown: true));
                var labelAfterCapture = button.Content;
                for (var repeat = 0; repeat < 5; repeat++)
                {
                    Assert.True(view.ProcessHotkeyInput(key, Key.None, ModifierKeys.Alt, isDown: true));
                    Assert.Equal(labelAfterCapture, button.Content);
                }
                Assert.Equal(new HotkeyBinding(KeyModifiers.Alt, key.ToString()), button.Tag);
                Assert.True(view.ProcessHotkeyInput(key, Key.None, ModifierKeys.None, isDown: false));
                Assert.False(view.ProcessHotkeyInput(key, Key.None, ModifierKeys.None, isDown: true));
                Assert.Equal(1, saved); // live apply: exactly one apply per capture, none for repeats
            }
            return true;
        });
    }

    [Fact]
    public void ShortcutRecorder_NormalizesSystemKeyForRepeatAndReleaseOwnership()
    {
        RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
            var button = (Button)view.FindName("ActivateHotkeyButton");
            view.BeginHotkeyCapture(button, requestFocus: false);
            Assert.True(view.ProcessHotkeyInput(Key.System, Key.F8, ModifierKeys.Alt, isDown: true));
            Assert.Equal(new HotkeyBinding(KeyModifiers.Alt, "F8"), button.Tag);
            Assert.True(view.ProcessHotkeyInput(Key.System, Key.F8, ModifierKeys.Alt, isDown: true));
            Assert.True(view.ProcessHotkeyInput(Key.System, Key.F8, ModifierKeys.None, isDown: false));
            Assert.False(view.ProcessHotkeyInput(Key.F8, Key.None, ModifierKeys.None, isDown: false));
            return true;
        });
    }

    [Fact]
    public void Window_UsesDwmBackdropMode_WithRoundedGlassAndSolidFallback()
    {
        var result = RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector())
            {
                SizeToContent = SizeToContent.Manual, Width = 456, Height = 700
            };
            try
            {
                view.UseBackdropFill(true);
                var shadowWhenAcrylic = ((UIElement)view.FindName("ShadowHost")).Visibility;
                var acrylicFill = ReferenceEquals(view.Resources["GlassFill"], view.Resources["GlassFillAcrylic"]);

                var root = (FrameworkElement)view.Content;
                view.Content = null;
                var host = new System.Windows.Controls.Border { Child = root, Width = 456, Height = 700 };
                TextElementSetup(host, view);
                host.Measure(new Size(456, 700));
                host.Arrange(new Rect(0, 0, 456, 700));
                host.UpdateLayout();
                var glass = (FrameworkElement)view.FindName("GlassBody");
                var glassBounds = glass.TransformToAncestor(host).TransformBounds(new Rect(0, 0, glass.ActualWidth, glass.ActualHeight));
                var bitmap = new RenderTargetBitmap(456, 700, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);
                var corner = ReadPixel(bitmap, (int)glassBounds.Left + 1, (int)glassBounds.Top + 1);
                var centre = ReadPixel(bitmap, (int)(glassBounds.Left + glassBounds.Width / 2), (int)(glassBounds.Top + glassBounds.Height / 2));

                view.UseBackdropFill(false);
                var shadowWhenSolid = ((UIElement)view.FindName("ShadowHost")).Visibility;
                var solidFill = ReferenceEquals(view.Resources["GlassFill"], view.Resources["GlassFillSolid"]);

                return (view.AllowsTransparency, shadowWhenAcrylic, acrylicFill, shadowWhenSolid, solidFill,
                    cornerAlpha: corner[3], centreAlpha: centre[3]);
            }
            finally
            {
                view.Close();
            }
        });
        Assert.True(result.AllowsTransparency);
        Assert.Equal(Visibility.Collapsed, result.shadowWhenAcrylic);
        Assert.True(result.acrylicFill);
        Assert.Equal(Visibility.Visible, result.shadowWhenSolid);
        Assert.True(result.solidFill);
        Assert.Equal(0, result.cornerAlpha);   // outside the 22 px radius the WPF layer is transparent
        Assert.InRange(result.centreAlpha, (byte)90, (byte)180); // translucent veil over DWM blur
    }

    [Fact]
    public void SettingsGelBackdrop_RendersDarkTranslucentGelWithRoundedCorners()
    {
        var darkBg = new BackdropTexture(64, 64) { OriginX = 0, OriginY = 0, PixelsPerTexelX = 4f, PixelsPerTexelY = 4f };
        darkBg.Fill(0.05f, 0.08f, 0.12f);
        var whiteBg = new BackdropTexture(64, 64) { OriginX = 0, OriginY = 0, PixelsPerTexelX = 4f, PixelsPerTexelY = 4f };
        whiteBg.Fill(1f, 1f, 1f);

        var bounds = new BackdropRegion(0, 0, 200, 160);
        var neutralDark = SettingsGelBackdrop.RenderGel(darkBg, bounds, 1f, 22f, GelBackdropState.Neutral);
        var neutralWhite = SettingsGelBackdrop.RenderGel(whiteBg, bounds, 1f, 22f, GelBackdropState.Neutral);

        // Corners outside 22 px radius must be completely transparent; interior opaque.
        Assert.Equal(0, ReadPixel(neutralWhite, 1, 1)[3]);
        Assert.Equal(255, ReadPixel(neutralWhite, 100, 80)[3]);

        // Translucent over white: brighter than over dark, yet darkened enough for light UI text.
        var darkCentre = ReadPixel(neutralDark, 100, 80);
        var whiteCentre = ReadPixel(neutralWhite, 100, 80);
        Assert.True(whiteCentre[2] > darkCentre[2] + 20, "White background should shine through the translucent gel.");
        Assert.True(whiteCentre[2] < 130, "Dark gel must stay dark enough for light text even on pure white.");
    }

    private static byte[] ReadPixel(BitmapSource bitmap, int x, int y)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    [Fact]
    public void Window_CanResize_WithMinimumSizeAndGrip()
    {
        var result = RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
            return (view.MinWidth, view.MinHeight, maxHeight: view.MaxHeight, workHeight: SystemParameters.WorkArea.Height, hasGrip: view.FindName("ResizeGrip") is not null);
        });
        Assert.Equal(400, result.MinWidth);
        Assert.Equal(380, result.MinHeight);
        Assert.True(result.maxHeight <= result.workHeight);
        Assert.True(result.hasGrip);
    }

    [Fact]
    public void ResponsiveLayout_SwitchesBetweenCompactAndThreeColumnWide()
    {
        var result = RunSta(() =>
        {
            var view = new SettingsView(new UserSettings(), Devices, new FakeProtector());
            var root = (FrameworkElement)view.Content;
            view.Content = null;
            var host = new Border { Child = root, Width = 456, Height = 700 };
            TextElementSetup(host, view);
            host.Measure(new Size(456, 700));
            host.Arrange(new Rect(0, 0, 456, 700));
            host.UpdateLayout();

            var hotkeysGrid = (Grid)view.FindName("HotkeysGrid");
            var behaviorSection = (FrameworkElement)view.FindName("BehaviorSection");
            var activate = (UIElement)view.FindName("ActivateHotkeyButton");
            var cancel = (UIElement)view.FindName("CancelHotkeyButton");
            var confirm = (UIElement)view.FindName("ConfirmHotkeyButton");
            var compactCols = hotkeysGrid.ColumnDefinitions.Count;
            var compactBehaviorCol = Grid.GetColumn(behaviorSection);

            host.Width = 760;
            host.Measure(new Size(760, 700));
            host.Arrange(new Rect(0, 0, 760, 700));
            host.UpdateLayout();

            var wideCols = hotkeysGrid.ColumnDefinitions.Count;
            var wideBehaviorCol = Grid.GetColumn(behaviorSection);
            var buttonRows = (Grid.GetRow(activate), Grid.GetRow(cancel), Grid.GetRow(confirm));
            var buttonCols = (Grid.GetColumn(activate), Grid.GetColumn(cancel), Grid.GetColumn(confirm));
            return (compactCols, compactBehaviorCol, wideCols, wideBehaviorCol, buttonRows, buttonCols);
        });

        Assert.Equal(2, result.compactCols);
        Assert.Equal(0, result.compactBehaviorCol);
        Assert.Equal(5, result.wideCols); // 3 button columns + 2 spacers
        Assert.Equal(2, result.wideBehaviorCol);
        Assert.Equal((1, 1, 1), result.buttonRows);
        Assert.Equal((0, 2, 4), result.buttonCols);
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
        // 820 leaves room for the sound theme selector, the drop style selector and the keyboard-language option.
        Assert.InRange(size.Height, 300, 820);
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
