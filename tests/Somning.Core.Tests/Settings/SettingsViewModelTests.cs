using Somning.Core.Settings;
using Somning.Core.Trigger;
using Xunit;

namespace Somning.Core.Tests.Settings;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void InitializesFromUserSettings_Correctly()
    {
        var settings = new UserSettings
        {
            Hotkey = HotkeyBinding.Default,
            MicrophoneDeviceId = "endpoint-1",
            AutoPaste = false,
            PlaySoundCues = true,
            StartWithWindows = true,
            CustomVocabulary = "Kubernetes, Docker",
            CloudProvider = "groq"
        };

        var vm = new SettingsViewModel(settings, ["endpoint-1", "endpoint-2"]);

        Assert.Equal("endpoint-1", vm.SelectedDeviceId);
        Assert.False(vm.AutoPaste);
        Assert.True(vm.PlaySoundCues);
        Assert.True(vm.StartWithWindows);
        Assert.Equal("Kubernetes, Docker", vm.CustomVocabulary);
        Assert.Equal("groq", vm.CloudProvider);
        Assert.Null(vm.ConflictWarning);
    }

    [Fact]
    public void UpdatingHotkeyToWindowsShortcut_DisplaysConflictWarning()
    {
        var vm = new SettingsViewModel(new UserSettings());
        Assert.Null(vm.ConflictWarning);

        // Win + L is locked by Windows (HOTK-05)
        vm.UpdateHotkey(new HotkeyBinding(KeyModifiers.Windows, "l"));

        Assert.NotNull(vm.ConflictWarning);
        Assert.Contains("bloqueia o Windows", vm.ConflictWarning);
    }

    [Fact]
    public void BuildUpdatedSettings_PreservesChanges()
    {
        var initial = new UserSettings();
        var vm = new SettingsViewModel(initial)
        {
            SelectedDeviceId = "mic-usb",
            AutoPaste = false,
            PlaySoundCues = true,
            StartWithWindows = true,
            CustomVocabulary = "  Termo Especial  ",
            CloudProvider = "openai"
        };

        var updated = vm.BuildUpdatedSettings(encryptedApiKey: "dpapi-cipher");

        Assert.Equal("mic-usb", updated.MicrophoneDeviceId);
        Assert.False(updated.AutoPaste);
        Assert.True(updated.PlaySoundCues);
        Assert.True(updated.StartWithWindows);
        Assert.Equal("Termo Especial", updated.CustomVocabulary);
        Assert.Equal("openai", updated.CloudProvider);
        Assert.Equal("dpapi-cipher", updated.EncryptedApiKey);
    }

    [Fact]
    public void StartWithWindows_DefaultsToFalseAndRoundTripsThroughBuild()
    {
        var vm = new SettingsViewModel(new UserSettings());
        Assert.False(vm.StartWithWindows);
        Assert.False(vm.BuildUpdatedSettings().StartWithWindows);

        vm.StartWithWindows = true;

        Assert.True(vm.BuildUpdatedSettings().StartWithWindows);
        // The initial settings are immutable and left untouched.
        Assert.False(vm.CurrentSettings.StartWithWindows);
    }
}
