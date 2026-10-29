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
        var settings = new UserSettings { MicrophoneDeviceId = "dev-1", CloudProvider = "openai", EncryptedApiKey = "enc:x", PlaySoundCues = true };

        var bytes = RunSta(() =>
        {
            var view = new SettingsView(settings, Devices, new FakeProtector());
            var root = (FrameworkElement)view.Content;
            view.Content = null;
            var host = new System.Windows.Controls.Border { Background = view.Background, Child = root, Width = view.Width, Height = 1180 };
            TextElementSetup(host, view);
            host.Measure(new Size(host.Width, host.Height));
            host.Arrange(new Rect(0, 0, host.Width, host.Height));
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        });

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllBytes(output, bytes);
        Assert.True(bytes.Length > 10_000);
    }

    private static void TextElementSetup(FrameworkElement host, Window view)
    {
        host.Resources.MergedDictionaries.Add(view.Resources);
        System.Windows.Documents.TextElement.SetForeground(host, view.Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(host, view.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(host, view.FontSize);
    }
}
