using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Somning.Core.Overlay;
using Somning.Core.Settings;
using Somning.Platform.Windows.Audio;
using Somning.Platform.Windows.Overlay;

namespace Somning.Platform.Windows.Settings;

/// <summary>WPF settings window themed after the Somning glass drop. Maps <see cref="UserSettings"/> to controls and back.</summary>
public partial class SettingsView : Window
{
    /// <summary>Shown in the key box when an encrypted key already exists; saving it unchanged keeps the stored key.</summary>
    public const string StoredKeyMask = "********";

    private static readonly string[] ProviderIds = ["none", "groq", "openai"];

    private readonly ISettingsProtector protector;
    private UserSettings original;
    private readonly IReadOnlyList<AudioDeviceInfo> devices;

    private const double WideLayoutBreakpoint = 720;

    private Button? capturingHotkey;
    private Key ownedCaptureKey = Key.None;
    private BackdropTexture? capturedDesktop;
    private WriteableBitmap? gelBitmap;
    private byte[]? gelBuffer;
    private bool gelAnimating;
    private long lastGelFrameTicks;
    private static readonly long GelFrameTicks = Stopwatch.Frequency / 60;
    private float tiltX, tiltY, tiltVelX, tiltVelY;
    private double lastMoveLeft, lastMoveTop;
    private bool isClosed;
    private bool isWideLayout;
    private bool isResizing;
    private Point resizeStartScreen;
    private double resizeStartWidth;
    private double resizeStartHeight;
    private volatile bool isCapturing;
    private readonly DispatcherTimer applyTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // Manual move instead of DragMove(): DragMove blocks until the mouse is released, so the glass could not follow it.
    private bool isMoving;
    private Point moveStartCursor;
    private double moveStartLeft;
    private double moveStartTop;

    /// <summary>Raised every time the form changes to a valid state: settings apply live, there is no Save step.</summary>
    public event Action<UserSettings>? Saved;

    /// <summary>True while the shortcut recorder waits for keys; the global hook must then leave the keyboard alone.</summary>
    public bool IsCapturingHotkey => isCapturing;

    public SettingsView(UserSettings settings, IReadOnlyList<AudioDeviceInfo> devices, ISettingsProtector protector)
    {
        InitializeComponent();
        Opacity = 0; // hide until Loaded finishes the first desktop capture so we never self-capture
        original = settings;
        this.devices = devices;
        this.protector = protector;
        UpdateWorkAreaLimits();
        UseBackdropFill(acrylic: false);
        Populate();
        ProviderCombo.SelectionChanged += (_, _) => UpdateProviderHint();
        DoneButton.Click += (_, _) => Close();
        WireLiveApply();
        CloseButton.Click += (_, _) => Close();
        Header.MouseLeftButtonDown += StartMove;
        Header.MouseMove += PerformMove;
        Header.MouseLeftButtonUp += EndMove;
        Header.LostMouseCapture += (_, _) =>
        {
            if (isMoving) EndMoveState();
        };
        Frame.SizeChanged += (_, e) => UpdateResponsiveLayout(e.NewSize.Width);
        ResizeGrip.MouseLeftButtonDown += StartResize;
        ResizeGrip.MouseMove += PerformResize;
        ResizeGrip.MouseLeftButtonUp += EndResize;
        ResizeGrip.LostMouseCapture += (_, _) =>
        {
            if (!isResizing) return;
            isResizing = false;
            RenderGelFrame(force: true);
        };
        Glass.SizeChanged += (_, _) => UpdateGlassGeometry();
        SourceInitialized += (_, _) => UpdateWorkAreaLimits();
        Loaded += (_, _) =>
        {
            UpdateWorkAreaLimits();
            if (ActualHeight >= MaxHeight - 0.5 && ActualHeight > MinHeight)
            {
                ContentRow.Height = new GridLength(1, GridUnitType.Star);
                Height = MaxHeight;
                SizeToContent = SizeToContent.Manual;
            }
            UpdateGlassGeometry();
            try
            {
                CaptureInitialBackdrop();
            }
            finally
            {
                Opacity = 1; // never leave the window invisible, even when capture fails
            }
        };
        ContentRendered += (_, _) => { if (Opacity < 1) Opacity = 1; };
        DpiChanged += (_, _) =>
        {
            UpdateWorkAreaLimits();
            RenderGelFrame(force: true);
        };
        Closed += (_, _) =>
        {
            isClosed = true;
            isCapturing = false;
            applyTimer.Stop();
            StopGelAnimation();
            capturedDesktop = null;
            gelBitmap = null;
            gelBuffer = null;
        };
        Closing += (_, _) => FlushPendingApply();
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

    /// <summary>Applies a rendered gel backdrop image, or hides the layer when null.</summary>
    public void ApplyBackdrop(BitmapSource? image)
    {
        BackdropImage.Source = image;
        if (image is not null)
        {
            UseBackdropFill(true);
        }
        else
        {
            BackdropHost.Visibility = Visibility.Collapsed;
            GlassBody.Opacity = 1;
        }
    }

    /// <summary>
    /// Switches between the dark frosted-jelly fill (rendered over the blurred desktop) and the semi-opaque fallback
    /// fill with a soft outer shadow. Public for off-screen previews.
    /// </summary>
    public void UseBackdropFill(bool acrylic)
    {
        isAcrylic = acrylic;
        Resources["GlassFill"] = Resources[acrylic ? "GlassFillAcrylic" : "GlassFillSolid"];
        var hasGelImage = acrylic && BackdropImage.Source is not null;
        BackdropHost.Visibility = hasGelImage ? Visibility.Visible : Visibility.Collapsed;
        GlassBody.Opacity = hasGelImage ? 0 : 1;
        ShadowHost.Visibility = acrylic ? Visibility.Collapsed : Visibility.Visible;
        UpdateGlassGeometry();
    }

    private void UpdateGlassGeometry()
    {
        if (SizeToContent == SizeToContent.Manual && !ContentRow.Height.IsStar)
            ContentRow.Height = new GridLength(1, GridUnitType.Star);
        var w = Glass.ActualWidth;
        var h = Glass.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var glass = new RectangleGeometry(new Rect(0, 0, w, h), GlassRadius, GlassRadius);
        BackdropHost.Clip = glass;
        InnerShadowHost.Clip = glass;
        // Shadow only outside the glass so the translucent body is not darkened from below.
        ShadowHost.Clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-60, -60, w + 120, h + 120)), glass);
        if (IsLoaded && IsVisible) RenderGelFrame(force: false);
    }

    private void CaptureInitialBackdrop()
    {
        if (isClosed || ActualWidth <= 0 || ActualHeight <= 0) return;
        var dpiScale = (float)VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpiScale <= 0f) dpiScale = 1f;

        using var source = new ScreenBackdropSource();
        capturedDesktop = SettingsGelBackdrop.CaptureBlurredArea(source, GetVirtualScreenBounds(), dpiScale);
        RenderGelFrame(force: true);
    }

    private void RenderGelFrame(bool force)
    {
        if (isClosed || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (PresentationSource.FromVisual(this) is null) return;
        var now = Stopwatch.GetTimestamp();
        if (!force && now - lastGelFrameTicks < GelFrameTicks) return;
        lastGelFrameTicks = now;

        var topLeft = PointToScreen(new Point(0, 0));
        var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        var windowBounds = new BackdropRegion(
            (int)Math.Round(topLeft.X),
            (int)Math.Round(topLeft.Y),
            Math.Max(1, (int)Math.Round(bottomRight.X - topLeft.X)),
            Math.Max(1, (int)Math.Round(bottomRight.Y - topLeft.Y)));
        var dpiScale = (float)VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpiScale <= 0f) dpiScale = 1f;

        var state = new GelBackdropState(tiltX, tiltY);
        gelBitmap = SettingsGelBackdrop.RenderInto(
            gelBitmap, capturedDesktop, windowBounds, dpiScale, (float)GlassRadius, state, ref gelBuffer);
        if (!ReferenceEquals(BackdropImage.Source, gelBitmap))
            ApplyBackdrop(gelBitmap);
    }

    private void StartGelAnimation()
    {
        if (gelAnimating || isClosed) return;
        gelAnimating = true;
        CompositionTarget.Rendering += OnGelAnimationFrame;
    }

    private void StopGelAnimation()
    {
        if (!gelAnimating) return;
        gelAnimating = false;
        CompositionTarget.Rendering -= OnGelAnimationFrame;
    }

    private void OnGelAnimationFrame(object? sender, EventArgs e)
    {
        if (isClosed)
        {
            StopGelAnimation();
            return;
        }
        var now = Stopwatch.GetTimestamp();
        if (now - lastGelFrameTicks < GelFrameTicks) return;

        // Spring-damper inertia tilt (liquid slosh when dragging).
        tiltVelX = (tiltVelX - tiltX * 0.16f) * 0.80f;
        tiltVelY = (tiltVelY - tiltY * 0.16f) * 0.80f;
        tiltX = Math.Clamp(tiltX + tiltVelX, -1f, 1f);
        tiltY = Math.Clamp(tiltY + tiltVelY, -1f, 1f);

        RenderGelFrame(force: true);

        var tiltSettled = MathF.Abs(tiltX) < 0.005f && MathF.Abs(tiltY) < 0.005f
            && MathF.Abs(tiltVelX) < 0.005f && MathF.Abs(tiltVelY) < 0.005f;
        if (tiltSettled)
        {
            tiltX = tiltY = tiltVelX = tiltVelY = 0f;
            StopGelAnimation();
        }
    }

    private static BackdropRegion GetVirtualScreenBounds() => new(
        GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private void UpdateWorkAreaLimits()
    {
        var work = GetWorkAreaDip();
        MaxWidth = Math.Max(MinWidth, work.Width - 16);
        MaxHeight = Math.Max(MinHeight, work.Height - 16);
    }

    private Rect GetWorkAreaDip()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
            if (monitor != 0 && GetMonitorInfo(monitor, ref info))
            {
                var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var tl = transform.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
                var br = transform.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));
                return new Rect(tl, br);
            }
        }
        return SystemParameters.WorkArea;
    }

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

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
        PopulateSoundThemes(original.SoundTheme);
        PopulateDropStyles(original.DropStyle);
        PreserveLanguageCheck.IsChecked = original.PreserveInputLanguage;
        StartWithWindowsCheck.IsChecked = original.StartWithWindows;
        CheckForUpdatesCheck.IsChecked = original.CheckForUpdates;
    }

    private void WireLiveApply()
    {
        applyTimer.Tick += (_, _) => { applyTimer.Stop(); ApplyNow(); };
        void Changed(object? sender, EventArgs e) => ApplyNow();
        void Typing(object? sender, EventArgs e) { applyTimer.Stop(); applyTimer.Start(); }
        DeviceCombo.SelectionChanged += Changed;
        ProviderCombo.SelectionChanged += Changed;
        SoundThemeCombo.SelectionChanged += Changed;
        DropStyleCombo.SelectionChanged += Changed;
        foreach (var check in new[] { AutoPasteCheck, SoundCuesCheck, PreserveLanguageCheck, StartWithWindowsCheck, CheckForUpdatesCheck })
        {
            check.Checked += Changed;
            check.Unchecked += Changed;
        }
        VocabularyBox.TextChanged += Typing;
        ApiKeyBox.PasswordChanged += Typing;
    }

    private void FlushPendingApply()
    {
        if (!applyTimer.IsEnabled) return;
        applyTimer.Stop();
        ApplyNow();
    }

    /// <summary>Applies the current form when valid. Invalid shortcut combinations are shown but never applied.</summary>
    private void ApplyNow()
    {
        if (isClosed || !ValidateHotkeys()) return;
        var updated = BuildUpdated();
        original = updated;
        Saved?.Invoke(updated);
    }

    private void PopulateSoundThemes(string selectedTheme)
    {
        foreach (var (id, displayName) in SoundThemes.All)
            SoundThemeCombo.Items.Add(new ComboBoxItem { Content = displayName, Tag = id });
        var index = SoundThemes.All.Select(theme => theme.Id).ToList().IndexOf(selectedTheme);
        SoundThemeCombo.SelectedIndex = index < 0 ? 0 : index;
    }

    private void PopulateDropStyles(string selectedStyle)
    {
        foreach (var (id, displayName) in DropStyles.All)
            DropStyleCombo.Items.Add(new ComboBoxItem { Content = displayName, Tag = id });
        var index = DropStyles.All.Select(style => style.Id).ToList().IndexOf(DropStyles.Normalize(selectedStyle));
        DropStyleCombo.SelectedIndex = index < 0 ? 0 : index;
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
            SoundTheme = (SoundThemeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? SoundThemes.Classic,
            DropStyle = DropStyles.Normalize((DropStyleCombo.SelectedItem as ComboBoxItem)?.Tag as string),
            PreserveInputLanguage = PreserveLanguageCheck.IsChecked == true,
            StartWithWindows = StartWithWindowsCheck.IsChecked == true,
            CheckForUpdates = CheckForUpdatesCheck.IsChecked == true,
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
        isCapturing = true;
        button.Content = "Pressione a combinação…";
        if (requestFocus) button.Focus();
        HotkeyHint.Text = "Pressione as teclas juntas. Clique fora para desistir.";
    }

    private void EndHotkeyCapture()
    {
        if (capturingHotkey is not null)
            SetHotkeyButton(capturingHotkey, (HotkeyBinding)capturingHotkey.Tag);
        capturingHotkey = null;
        isCapturing = false;
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
        ApplyNow();
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
        return valid;
    }

    private void StartMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        isMoving = true;
        moveStartCursor = CursorInDips();
        moveStartLeft = Left;
        moveStartTop = Top;
        lastMoveLeft = Left;
        lastMoveTop = Top;
        Header.CaptureMouse();
        e.Handled = true;
    }

    private void PerformMove(object sender, MouseEventArgs e)
    {
        if (!isMoving || !Header.IsMouseCaptured) return;
        var cursor = CursorInDips();
        var nextLeft = moveStartLeft + (cursor.X - moveStartCursor.X);
        var nextTop = moveStartTop + (cursor.Y - moveStartCursor.Y);
        var vx = (float)(nextLeft - lastMoveLeft);
        var vy = (float)(nextTop - lastMoveTop);
        lastMoveLeft = nextLeft;
        lastMoveTop = nextTop;
        Left = nextLeft;
        Top = nextTop;

        // Feed drag velocity into liquid slosh inertia and render immediately (no frame drop / misalignment).
        tiltVelX = Math.Clamp(tiltVelX + vx * 0.014f, -0.55f, 0.55f);
        tiltVelY = Math.Clamp(tiltVelY + vy * 0.014f, -0.55f, 0.55f);
        RenderGelFrame(force: true);
        StartGelAnimation();
    }

    private void EndMove(object sender, MouseButtonEventArgs e)
    {
        if (!isMoving) return;
        EndMoveState();
        e.Handled = true;
    }

    private void EndMoveState()
    {
        isMoving = false;
        if (Header.IsMouseCaptured) Header.ReleaseMouseCapture();
        RenderGelFrame(force: true);
    }

    /// <summary>Cursor position in device-independent pixels, read from the OS so it does not move with the window.</summary>
    private Point CursorInDips()
    {
        GetCursorPos(out var cursor);
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Point(cursor.X * transform.M11, cursor.Y * transform.M22);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);

    private void StartResize(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        EndHotkeyCapture();
        isResizing = true;
        resizeStartScreen = PointToScreen(e.GetPosition(this));
        resizeStartWidth = ActualWidth;
        resizeStartHeight = ActualHeight;
        ResizeGrip.CaptureMouse();
        e.Handled = true;
    }

    private void PerformResize(object sender, MouseEventArgs e)
    {
        if (!isResizing || !ResizeGrip.IsMouseCaptured) return;
        var currentScreen = PointToScreen(e.GetPosition(this));
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var dx = (currentScreen.X - resizeStartScreen.X) * transform.M11;
        var dy = (currentScreen.Y - resizeStartScreen.Y) * transform.M22;
        if (dx == 0 && dy == 0) return;
        // Keep SizeToContent=Height on initial open; switch to manual only after the user drags the grip.
        if (SizeToContent != SizeToContent.Manual)
        {
            ContentRow.Height = new GridLength(1, GridUnitType.Star);
            Width = resizeStartWidth;
            Height = resizeStartHeight;
            SizeToContent = SizeToContent.Manual;
        }
        var work = GetWorkAreaDip();
        var maxWidth = Math.Max(MinWidth, Math.Min(MaxWidth, work.Right - Left));
        var maxHeight = Math.Max(MinHeight, Math.Min(MaxHeight, work.Bottom - Top));
        Width = Math.Clamp(resizeStartWidth + dx, MinWidth, maxWidth);
        Height = Math.Clamp(resizeStartHeight + dy, MinHeight, maxHeight);
        RenderGelFrame(force: true);
    }

    private void EndResize(object sender, MouseButtonEventArgs e)
    {
        if (!isResizing) return;
        isResizing = false;
        if (ResizeGrip.IsMouseCaptured) ResizeGrip.ReleaseMouseCapture();
        RenderGelFrame(force: true);
        e.Handled = true;
    }

    private void UpdateResponsiveLayout(double width)
    {
        var wide = width >= WideLayoutBreakpoint;
        if (wide == isWideLayout) return;
        isWideLayout = wide;

        SectionsGrid.ColumnDefinitions.Clear();
        SectionsGrid.RowDefinitions.Clear();
        HotkeysGrid.ColumnDefinitions.Clear();
        HotkeysGrid.RowDefinitions.Clear();

        if (wide)
        {
            SectionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SectionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            SectionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SectionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SectionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SectionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            SetGridPosition(HotkeysSection, row: 0, column: 0, columnSpan: 3);
            SetGridPosition(InputSection, row: 1, column: 0);
            SetGridPosition(TranscriptionSection, row: 2, column: 0);
            SetGridPosition(BehaviorSection, row: 1, column: 2, rowSpan: 2);
            TranscriptionSection.Margin = new Thickness(0);

            for (var i = 0; i < 3; i++)
            {
                if (i > 0) HotkeysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
                HotkeysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            HotkeysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            HotkeysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            PlaceWideHotkeyRow(HotkeyActivateLabel, ActivateHotkeyButton, column: 0);
            PlaceWideHotkeyRow(HotkeyCancelLabel, CancelHotkeyButton, column: 2);
            PlaceWideHotkeyRow(HotkeyConfirmLabel, ConfirmHotkeyButton, column: 4);
        }
        else
        {
            SectionsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < 4; i++)
                SectionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            SetGridPosition(HotkeysSection, row: 0, column: 0);
            SetGridPosition(InputSection, row: 1, column: 0);
            SetGridPosition(TranscriptionSection, row: 2, column: 0);
            SetGridPosition(BehaviorSection, row: 3, column: 0);
            TranscriptionSection.Margin = new Thickness(0, 0, 0, 10);

            HotkeysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            HotkeysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            for (var i = 0; i < 3; i++)
                HotkeysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            PlaceCompactHotkeyRow(HotkeyActivateLabel, ActivateHotkeyButton, row: 0, bottomMargin: 4);
            PlaceCompactHotkeyRow(HotkeyCancelLabel, CancelHotkeyButton, row: 1, bottomMargin: 4);
            PlaceCompactHotkeyRow(HotkeyConfirmLabel, ConfirmHotkeyButton, row: 2, bottomMargin: 0);
        }
    }

    private static void SetGridPosition(UIElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
    }

    private static void PlaceWideHotkeyRow(TextBlock label, Button button, int column)
    {
        SetGridPosition(label, row: 0, column: column);
        label.VerticalAlignment = VerticalAlignment.Bottom;
        label.Margin = new Thickness(2, 0, 0, 5);
        SetGridPosition(button, row: 1, column: column);
        button.Margin = new Thickness(0);
    }

    private static void PlaceCompactHotkeyRow(TextBlock label, Button button, int row, double bottomMargin)
    {
        SetGridPosition(label, row: row, column: 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0);
        SetGridPosition(button, row: row, column: 1);
        button.Margin = new Thickness(0, 0, 0, bottomMargin);
    }
}

