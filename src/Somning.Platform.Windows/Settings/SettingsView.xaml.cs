using System.Windows;
using System.Windows.Media.Imaging;
using Somning.Core.Settings;
using Somning.Platform.Windows.Audio;

namespace Somning.Platform.Windows.Settings;

/// <summary>WPF settings window themed after the Somning glass drop. Maps <see cref="UserSettings"/> to controls and back.</summary>
public partial class SettingsView : Window
{
    /// <summary>Shown in the key box when an encrypted key already exists; saving it unchanged keeps the stored key.</summary>
    public const string StoredKeyMask = "********";

    private static readonly string[] ProviderIds = ["none", "groq", "openai"];

    private readonly ISettingsProtector protector;
    private readonly UserSettings original;
    private readonly IReadOnlyList<AudioDeviceInfo> devices;

    public event Action<UserSettings>? Saved;

    public SettingsView(UserSettings settings, IReadOnlyList<AudioDeviceInfo> devices, ISettingsProtector protector)
    {
        InitializeComponent();
        original = settings;
        this.devices = devices;
        this.protector = protector;
        BrandImage.Source = new BitmapImage(new Uri("pack://application:,,,/Somning.Platform.Windows;component/Branding/somning-logo.png"));
        Populate();
        ProviderCombo.SelectionChanged += (_, _) => UpdateProviderHint();
        SaveButton.Click += (_, _) => { Saved?.Invoke(BuildUpdated()); Close(); };
        CancelButton.Click += (_, _) => Close();
        UpdateProviderHint();
    }

    private void Populate()
    {
        // The keyboard hook currently honours only the default chord, so show what actually works.
        HotkeyText.Text = HotkeyDisplay.Format(HotkeyBinding.Default);

        DeviceCombo.Items.Add("Padrão do Windows");
        var selected = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            DeviceCombo.Items.Add(devices[i].Name);
            if (string.Equals(devices[i].Id, original.MicrophoneDeviceId, StringComparison.OrdinalIgnoreCase)) selected = i + 1;
        }
        DeviceCombo.SelectedIndex = selected;

        var provider = Array.IndexOf(ProviderIds, original.CloudProvider?.ToLowerInvariant());
        ProviderCombo.SelectedIndex = provider < 0 ? 0 : provider;
        ApiKeyBox.Password = string.IsNullOrEmpty(original.EncryptedApiKey) ? string.Empty : StoredKeyMask;
        VocabularyBox.Text = original.CustomVocabulary ?? string.Empty;
        AutoPasteCheck.IsChecked = original.AutoPaste;
        SoundCuesCheck.IsChecked = original.PlaySoundCues;
        StartWithWindowsCheck.IsChecked = original.StartWithWindows;
    }

    private void UpdateProviderHint()
    {
        var cloud = ProviderCombo.SelectedIndex > 0;
        ApiKeyBox.IsEnabled = cloud;
        ProviderHint.Text = cloud
            ? "O áudio é enviado ao provedor escolhido. A chave fica criptografada (DPAPI) no seu usuário do Windows."
            : "Tudo roda neste computador, offline, com o modelo Whisper local.";
    }

    /// <summary>Builds the updated settings from the current control state without mutating the original.</summary>
    public UserSettings BuildUpdated()
    {
        var deviceIndex = DeviceCombo.SelectedIndex;
        var deviceId = deviceIndex > 0 && deviceIndex - 1 < devices.Count ? devices[deviceIndex - 1].Id : null;
        var providerIndex = ProviderCombo.SelectedIndex;
        var key = ApiKeyBox.Password;
        var encrypted = key switch
        {
            "" => null,
            StoredKeyMask => original.EncryptedApiKey,
            _ => protector.Protect(key)
        };

        return original with
        {
            MicrophoneDeviceId = deviceId,
            AutoPaste = AutoPasteCheck.IsChecked == true,
            PlaySoundCues = SoundCuesCheck.IsChecked == true,
            StartWithWindows = StartWithWindowsCheck.IsChecked == true,
            CustomVocabulary = VocabularyBox.Text.Trim(),
            CloudProvider = providerIndex > 0 && providerIndex < ProviderIds.Length ? ProviderIds[providerIndex] : "none",
            EncryptedApiKey = encrypted
        };
    }
}
