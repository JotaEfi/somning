using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
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

    private Button? capturingHotkey;
    private Key ownedCaptureKey = Key.None;
    private HwndSource? nativeSource;
    private bool geometryQueued;
    private bool isClosed;
    public event Action<UserSettings>? Saved;

    public SettingsView(UserSettings settings, IReadOnlyList<AudioDeviceInfo> devices, ISettingsProtector protector)
    {
        InitializeComponent();
        original = settings;
        this.devices = devices;
        this.protector = protector;
        UseBackdropFill(acrylic: false);
        Populate();
        ProviderCombo.SelectionChanged += (_, _) => UpdateProviderHint();
        SaveButton.Click += (_, _) => { if (!ValidateHotkeys()) return; Saved?.Invoke(BuildUpdated()); Close(); };
        CancelButton.Click += (_, _) => Close();
        CloseButton.Click += (_, _) => Close();
        Header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Glass.SizeChanged += (_, _) => UpdateGlassGeometry();
        SourceInitialized += (_, _) =>
        {
            nativeSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            nativeSource?.AddHook(NativeWindowMessage);
            UseBackdropFill(GlassBackdrop.TryEnable(this));
            QueueGlassGeometry();
        };
        ContentRendered += (_, _) => QueueGlassGeometry();
        DpiChanged += (_, _) => QueueGlassGeometry();
        Closed += (_, _) => { isClosed = true; nativeSource?.RemoveHook(NativeWindowMessage); };
        ActivateHotkeyButton.Click += (_, _) => BeginHotkeyCapture(ActivateHotkeyButton);
        CancelHotkeyButton.Click += (_, _) => BeginHotkeyCapture(CancelHotkeyButton);
        ConfirmHotkeyButton.Click += (_, _) => BeginHotkeyCapture(ConfirmHotkeyButton);
        PreviewKeyDown += CaptureHotkey;
        PreviewKeyUp += ReleaseHotkey;
        PreviewMouseDown += (_, _) => EndHotkeyCapture();
        UpdateProviderHint();
    }

    /// <summary>Glass radius shared by the XAML layers, clips and the window region.</summary>
    private const double GlassRadius = 22;

    private bool isAcrylic;

    /// <summary>
    /// Switches between the real-blur fill (more transparent, region-clipped, no drop shadow since the region
    /// would cut it) and the semi-opaque fallback fill with a soft outer shadow. Public for off-screen previews.
    /// </summary>
    public void UseBackdropFill(bool acrylic)
    {
        isAcrylic = acrylic;
        Resources["GlassFill"] = Resources[acrylic ? "GlassFillAcrylic" : "GlassFillSolid"];
        ShadowHost.Visibility = acrylic ? Visibility.Collapsed : Visibility.Visible;
        UpdateGlassGeometry();
    }

    private void UpdateGlassGeometry()
    {
        var w = Glass.ActualWidth;
        var h = Glass.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var glass = new RectangleGeometry(new Rect(0, 0, w, h), GlassRadius, GlassRadius);
        InnerShadowHost.Clip = glass;
        // Shadow only outside the glass so the translucent body is not darkened from below.
        ShadowHost.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-60, -60, w + 120, h + 120)), glass);
        if (new WindowInteropHelper(this).Handle != 0)
        {
            // The borderless client is exactly the Frame. This also works for a native
            // handle created without Show(), before WPF attaches the window visual.
            var origin = Glass.TranslatePoint(new Point(0, 0), Frame);
            GlassBackdrop.ClipToRoundedRect(this, new Rect(origin, new Size(w, h)), GlassRadius);
        }
    }

    private void Populate()
    {
        SetHotkeyButton(ActivateHotkeyButton, original.Hotkey);
        SetHotkeyButton(CancelHotkeyButton, original.CancelHotkey);
        SetHotkeyButton(ConfirmHotkeyButton, original.ConfirmHotkey);
        VersionText.Text = typeof(SettingsView).Assembly.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.1";

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

        var updated = original with
        {
            Hotkey = (HotkeyBinding)ActivateHotkeyButton.Tag,
            CancelHotkey = (HotkeyBinding)CancelHotkeyButton.Tag,
            ConfirmHotkey = (HotkeyBinding)ConfirmHotkeyButton.Tag,
            MicrophoneDeviceId = deviceId,
            AutoPaste = AutoPasteCheck.IsChecked == true,
            PlaySoundCues = SoundCuesCheck.IsChecked == true,
            StartWithWindows = StartWithWindowsCheck.IsChecked == true,
            CustomVocabulary = VocabularyBox.Text.Trim(),
            CloudProvider = providerIndex > 0 && providerIndex < ProviderIds.Length ? ProviderIds[providerIndex] : "none",
            EncryptedApiKey = encrypted
        };
        if (!HotkeyBindings.TryValidate(updated, out var error)) throw new InvalidOperationException(error);
        return updated;
    }
    private static void SetHotkeyButton(Button button, HotkeyBinding binding)
    {
        button.Tag = binding;
        button.Content = HotkeyDisplay.Format(binding);
    }

    internal void BeginHotkeyCapture(Button button, bool requestFocus = true)
    {
        EndHotkeyCapture();
        capturingHotkey = button;
        button.Content = "Pressione a combinação…";
        if (requestFocus) button.Focus();
        HotkeyHint.Text = "Pressione as teclas juntas. Clique fora para desistir.";
    }

    private void EndHotkeyCapture()
    {
        if (capturingHotkey is not null)
            SetHotkeyButton(capturingHotkey, (HotkeyBinding)capturingHotkey.Tag);
        capturingHotkey = null;
        HotkeyHint.Text = "Clique para alterar. Ativar: segure ou toque. Confirmar: encerrar e transcrever.";
    }

    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        if (ProcessHotkeyInput(e.Key, e.SystemKey, Keyboard.Modifiers, isDown: true)) e.Handled = true;
    }

    private void ReleaseHotkey(object sender, KeyEventArgs e)
    {
        if (ProcessHotkeyInput(e.Key, e.SystemKey, Keyboard.Modifiers, isDown: false)) e.Handled = true;
    }

    // The same ownership contract is used for live WPF events and headless managed tests.
    // A candidate is accepted on key-down, but repeats and its eventual key-up still belong
    // to the recorder: Space must not reopen the button and Enter must not save the form.
    internal bool ProcessHotkeyInput(Key key, Key systemKey, ModifierKeys keyboardModifiers, bool isDown)
    {
        key = key == Key.System ? systemKey : key;
        if (ownedCaptureKey != Key.None && key == ownedCaptureKey)
        {
            if (!isDown) ownedCaptureKey = Key.None;
            return true;
        }
        if (capturingHotkey is null) return false;
        if (!isDown) return true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.None
            or Key.ImeProcessed or Key.DeadCharProcessed) return true;
        var modifiers = KeyModifiers.None;
        if ((keyboardModifiers & ModifierKeys.Control) != 0) modifiers |= KeyModifiers.Control;
        if ((keyboardModifiers & ModifierKeys.Alt) != 0) modifiers |= KeyModifiers.Alt;
        if ((keyboardModifiers & ModifierKeys.Shift) != 0) modifiers |= KeyModifiers.Shift;
        if ((keyboardModifiers & ModifierKeys.Windows) != 0) modifiers |= KeyModifiers.Windows;
        ownedCaptureKey = key;
        var target = capturingHotkey;
        SetHotkeyButton(target, new HotkeyBinding(modifiers, key.ToString()));
        capturingHotkey = null;
        EndHotkeyCapture();
        ValidateHotkeys();
        return true;
    }

    private bool ValidateHotkeys()
    {
        var candidate = original with
        {
            Hotkey = (HotkeyBinding)ActivateHotkeyButton.Tag,
            CancelHotkey = (HotkeyBinding)CancelHotkeyButton.Tag,
            ConfirmHotkey = (HotkeyBinding)ConfirmHotkeyButton.Tag
        };
        var valid = HotkeyBindings.TryValidate(candidate, out var error);
        HotkeyValidation.Text = error ?? string.Empty;
        HotkeyValidation.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.IsEnabled = valid;
        return valid;
    }

    private void QueueGlassGeometry()
    {
        if (geometryQueued || isClosed || Dispatcher.HasShutdownStarted) return;
        geometryQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            geometryQueued = false;
            if (!isClosed) UpdateGlassGeometry();
        }));
    }

    private nint NativeWindowMessage(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        // Reapply after native size/DPI/style/composition processing. Equality checks in GlassBackdrop
        // prevent SetWindowRgn -> WM_WINDOWPOSCHANGED -> SetWindowRgn repaint loops.
        if (message is 0x0047 or 0x0005 or 0x02E0 or 0x007D or 0x031E) QueueGlassGeometry();
        return 0;
    }
}

