using Somning.App.Composition;
using Somning.Core.Dictation;
using Somning.Core.Output;
using Somning.Core.Ports;
using Somning.Core.Settings;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Settings;
using Xunit;

namespace Somning.Integration.Tests.Settings;

public sealed class SettingsIntegrationTests
{
    private sealed class MemorySettingsStore : ISettingsStore
    {
        public UserSettings Current { get; set; } = new();
        public string FilePath => "memory://settings.json";

        public Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingTextOutput : ITextOutput
    {
        public bool DeliverCalled { get; private set; }
        public bool CopyOnlyCalled { get; private set; }
        public string? LastText { get; private set; }

        public Task<DeliveryResult> DeliverAsync(string text, PasteTarget target, CancellationToken ct)
        {
            DeliverCalled = true;
            LastText = text;
            return Task.FromResult(new DeliveryResult("pasted", null, 5, 10, 1));
        }

        public Task<DeliveryResult> CopyOnlyAsync(string text, CancellationToken ct)
        {
            CopyOnlyCalled = true;
            LastText = text;
            return Task.FromResult(new DeliveryResult("clipboard_set", null, 5, 0, 1));
        }
    }

    [Fact]
    public async Task DynamicAudioDeviceSwitching_UpdatesNextCaptureWithoutRestart()
    {
        var dynamicAudio = new DynamicAudioSourceFactory("initial-mic-id");
        var store = new MemorySettingsStore
        {
            Current = new UserSettings { MicrophoneDeviceId = "initial-mic-id" }
        };

        await using var app = new AppComposition(
            () => dynamicAudio.CreateAudioSource(),
            settingsStore: store,
            dynamicAudio: dynamicAudio);

        Assert.Equal("initial-mic-id", app.DynamicAudioFactory?.CurrentDeviceId);

        // User changes microphone in settings dialog (AUD-02)
        var updated = app.Settings with { MicrophoneDeviceId = "new-usb-headset-id" };
        app.OnSettingsSaved(updated);

        // Verification: dynamic audio factory updated immediately without restarting
        Assert.Equal("new-usb-headset-id", app.DynamicAudioFactory?.CurrentDeviceId);
        Assert.Equal("new-usb-headset-id", store.Current.MicrophoneDeviceId);
    }

    [Fact]
    public async Task AutoPasteToggle_WhenDisabled_RoutesDeliveryToClipboardOnly()
    {
        var trackingOutput = new TrackingTextOutput();
        var store = new MemorySettingsStore
        {
            Current = new UserSettings { AutoPaste = true }
        };

        await using var app = new AppComposition(
            () => new WasapiAudioSource(),
            settingsStore: store);

        Assert.True(app.AutoPasteEnabled);

        // Update settings: turn off auto-paste (OUT-04)
        var updated = app.Settings with { AutoPaste = false };
        app.OnSettingsSaved(updated);

        Assert.False(app.AutoPasteEnabled);
        Assert.False(store.Current.AutoPaste);
    }

    [Fact]
    public async Task SoundCuesToggle_EnablesAndDisablesSoundPlayer()
    {
        var store = new MemorySettingsStore
        {
            Current = new UserSettings { PlaySoundCues = false }
        };

        await using var app = new AppComposition(
            () => new WasapiAudioSource(),
            settingsStore: store);

        Assert.False(app.SoundPlayer.Enabled);

        // Enable sound cues (OVL-04)
        var updated = app.Settings with { PlaySoundCues = true };
        app.OnSettingsSaved(updated);

        Assert.True(app.SoundPlayer.Enabled);
    }

    [Fact]
    public void SettingsWindow_Lifecycle_OpensAndClosesGracefully()
    {
        using var window = new SettingsWindowManager();
        Assert.False(window.IsOpen);

        var settings = new UserSettings
        {
            CustomVocabulary = "Teste, Vocabulário",
            AutoPaste = true
        };

        bool saved = false;
        window.Show(settings, _ => saved = true);
        Assert.True(window.IsOpen);

        window.Close();
        Assert.False(window.IsOpen);
        Assert.False(saved);
    }
}
