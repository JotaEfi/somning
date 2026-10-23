using Somning.Core.Settings;
using Xunit;

namespace Somning.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "somning-settings-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void UserSettings_DefaultValues_MatchPhase2Requirements()
    {
        var settings = new UserSettings();

        Assert.Equal(new HotkeyBinding(KeyModifiers.Control | KeyModifiers.Shift, "Space"), settings.Hotkey);
        Assert.Null(settings.MicrophoneDeviceId);
        Assert.Equal("vulkan", settings.EngineBackend);
        Assert.Equal("none", settings.CloudProvider);
        Assert.Null(settings.EncryptedApiKey);
        Assert.True(settings.AutoPaste);
        Assert.False(settings.PlaySoundCues);
        Assert.Equal(string.Empty, settings.CustomVocabulary);
        Assert.Equal(300, settings.TapThresholdMs);
        Assert.Equal("pt-BR", settings.Language);
    }

    [Fact]
    public async Task SettingsStore_MissingFile_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var cancellationToken = TestContext.Current.CancellationToken;

        var loaded = await store.LoadAsync(cancellationToken);

        Assert.Equal(new UserSettings(), loaded);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public async Task SettingsStore_RoundTrip_PreservesAllFields()
    {
        var store = new JsonSettingsStore(SettingsPath);
        var expected = new UserSettings
        {
            Hotkey = new HotkeyBinding(KeyModifiers.Alt | KeyModifiers.Windows, "F9"),
            MicrophoneDeviceId = "microphone-42",
            EngineBackend = "cpu",
            CloudProvider = "openai",
            EncryptedApiKey = "base64-dpapi-blob",
            AutoPaste = false,
            PlaySoundCues = true,
            CustomVocabulary = "Somning, OpenCode, Maestro",
            TapThresholdMs = 450,
            Language = "en-US"
        };

        var cancellationToken = TestContext.Current.CancellationToken;

        await store.SaveAsync(expected, cancellationToken);
        var loaded = await store.LoadAsync(cancellationToken);

        Assert.Equal(expected, loaded);
        Assert.True(File.Exists(SettingsPath));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task SettingsStore_CorruptJson_RecoversToDefaultAndArchivesCorruptFile()
    {
        const string corruptJson = "{ \"Language\": \"pt-BR\", \"Hotkey\": ";
        var cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(SettingsPath, corruptJson, cancellationToken);
        var store = new JsonSettingsStore(SettingsPath);

        var loaded = await store.LoadAsync(cancellationToken);

        Assert.Equal(new UserSettings(), loaded);
        Assert.False(File.Exists(SettingsPath));

        var archivedPath = Assert.Single(Directory.GetFiles(_directory, "settings.json.corrupt.*"));
        Assert.Equal(corruptJson, await File.ReadAllTextAsync(archivedPath, cancellationToken));
    }
}
