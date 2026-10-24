using System.Diagnostics;
using System.Runtime.InteropServices;
using Somning.Core.Settings;
using Somning.Platform.Windows.Audio;

namespace Somning.Platform.Windows.Settings;

public sealed class SettingsWindowManager : ISettingsWindow
{
    private const string WindowClassName = "Somning_Settings_Window";
    private const uint WmApp = 0x8000;
    private const uint WmAppShow = WmApp + 201;
    private const uint WmAppClose = WmApp + 202;
    private const uint WmAppQuit = WmApp + 203;

    private const uint WsOverlapped = 0x00000000;
    private const uint WsCaption = 0x00C00000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsVisible = 0x10000000;
    private const uint WsChild = 0x40000000;
    private const uint WsTabStop = 0x00010000;
    private const uint WsBorder = 0x00800000;

    private const uint BsAutoCheckBox = 0x00000003;
    private const uint BsDefPushButton = 0x00000001;
    private const uint BsPushButton = 0x00000000;
    private const uint CbsDropDownList = 0x00000003;
    private const uint EsAutoHScroll = 0x00000080;
    private const uint EsPassword = 0x00000020;

    private const uint CbAddString = 0x0143;
    private const uint CbSetCurSel = 0x014E;
    private const uint CbGetCurSel = 0x0147;
    private const uint BmSetCheck = 0x00F1;
    private const uint BmGetCheck = 0x00F0;
    private const uint WmSetFont = 0x0030;
    private const uint WmCommand = 0x0111;
    private const uint WmClose = 0x0010;

    private const int IdDeviceCombo = 1001;
    private const int IdAutoPasteCheck = 1002;
    private const int IdSoundCuesCheck = 1003;
    private const int IdVocabEdit = 1004;
    private const int IdProviderCombo = 1005;
    private const int IdApiKeyEdit = 1006;
    private const int IdSaveButton = 1007;
    private const int IdCancelButton = 1008;
    // 1009 avoids colliding with IdCancelButton (a duplicate id would make the checkbox close the dialog).
    private const int IdStartWithWindowsCheck = 1009;

    private readonly Thread staThread;
    private readonly TaskCompletionSource<nint> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly WindowProcedure wndProcDelegate;
    private readonly ISettingsProtector protector;
    private nint hwnd;
    private uint threadId;
    private bool disposed;
    private bool isOpen;

    private nint hDeviceCombo;
    private nint hAutoPasteCheck;
    private nint hSoundCuesCheck;
    private nint hStartWithWindowsCheck;
    private nint hVocabEdit;
    private nint hProviderCombo;
    private nint hApiKeyEdit;
    private nint hFont;

    private UserSettings? pendingSettings;
    private Action<UserSettings>? pendingOnSave;
    private IReadOnlyList<AudioDeviceInfo> cachedDevices = [];

    public SettingsWindowManager(ISettingsProtector? protector = null)
    {
        this.protector = protector ?? new DpapiSettingsProtector();
        wndProcDelegate = WndProc;
        staThread = new Thread(RunMessagePump)
        {
            IsBackground = true,
            Name = "Somning.Settings.STA"
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        hwnd = ready.Task.GetAwaiter().GetResult();
    }

    public bool IsOpen => isOpen;

    public void Show(UserSettings currentSettings, Action<UserSettings> onSave)
    {
        if (disposed) return;
        pendingSettings = currentSettings;
        pendingOnSave = onSave;
        isOpen = true;
        if (threadId != 0)
        {
            PostThreadMessageW(threadId, WmAppShow, 0, 0);
        }
    }

    public void Close()
    {
        if (disposed) return;
        isOpen = false;
        if (threadId != 0)
        {
            PostThreadMessageW(threadId, WmAppClose, 0, 0);
        }
    }

    private void RunMessagePump()
    {
        threadId = GetCurrentThreadId();
        var instance = GetModuleHandle(null);

        var wc = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Style = 0x0001 | 0x0002, // CS_VREDRAW | CS_HREDRAW
            Procedure = Marshal.GetFunctionPointerForDelegate(wndProcDelegate),
            Instance = instance,
            ClassName = WindowClassName,
            Background = 15 // COLOR_BTNFACE + 1
        };

        _ = RegisterClassEx(in wc);

        var style = WsOverlapped | WsCaption | WsSysMenu | WsMinimizeBox;
        hwnd = CreateWindowEx(
            0,
            WindowClassName,
            "Configurações - Somning",
            style,
            150, 150, 420, 520,
            0, 0, instance, 0);

        if (hwnd == 0)
        {
            ready.TrySetException(new System.ComponentModel.Win32Exception());
            return;
        }

        CreateChildControls(instance);
        ready.TrySetResult(hwnd);

        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            if (msg.Msg == WmAppShow)
            {
                PopulateControls();
                ShowWindow(hwnd, 5 /* SW_SHOW */);
                SetForegroundWindow(hwnd);
            }
            else if (msg.Msg == WmAppClose)
            {
                ShowWindow(hwnd, 0 /* SW_HIDE */);
            }
            else if (msg.Msg == WmAppQuit)
            {
                break;
            }

            TranslateMessage(in msg);
            DispatchMessage(in msg);
        }

        if (hwnd != 0)
        {
            DestroyWindow(hwnd);
            hwnd = 0;
        }

        if (hFont != 0)
        {
            DeleteObject(hFont);
            hFont = 0;
        }
    }

    private void CreateChildControls(nint instance)
    {
        hFont = CreateFontW(-14, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");

        // Labels and inputs
        CreateLabel(hwnd, instance, "Microfone:", 20, 15, 360, 20);
        hDeviceCombo = CreateWindowEx(0, "COMBOBOX", "", WsChild | WsVisible | WsTabStop | CbsDropDownList, 20, 38, 360, 200, hwnd, (nint)IdDeviceCombo, instance, 0);

        CreateLabel(hwnd, instance, "Atalho de Gravação:", 20, 75, 360, 20);
        var hHotkeyLabel = CreateWindowEx(0, "STATIC", "Ctrl + Space (Padrão)", WsChild | WsVisible, 20, 98, 360, 20, hwnd, 0, instance, 0);

        hAutoPasteCheck = CreateWindowEx(0, "BUTTON", "Colar automaticamente no aplicativo ativo", WsChild | WsVisible | WsTabStop | BsAutoCheckBox, 20, 130, 360, 24, hwnd, (nint)IdAutoPasteCheck, instance, 0);
        hSoundCuesCheck = CreateWindowEx(0, "BUTTON", "Emitir sons de início e término", WsChild | WsVisible | WsTabStop | BsAutoCheckBox, 20, 160, 360, 24, hwnd, (nint)IdSoundCuesCheck, instance, 0);
        hStartWithWindowsCheck = CreateWindowEx(0, "BUTTON", "Iniciar com o Windows", WsChild | WsVisible | WsTabStop | BsAutoCheckBox, 20, 190, 360, 24, hwnd, (nint)IdStartWithWindowsCheck, instance, 0);

        CreateLabel(hwnd, instance, "Vocabulário / Termos específicos:", 20, 225, 360, 20);
        hVocabEdit = CreateWindowEx(0, "EDIT", "", WsChild | WsVisible | WsTabStop | WsBorder | EsAutoHScroll, 20, 248, 360, 24, hwnd, (nint)IdVocabEdit, instance, 0);

        CreateLabel(hwnd, instance, "Provedor de Transcrição:", 20, 285, 170, 20);
        hProviderCombo = CreateWindowEx(0, "COMBOBOX", "", WsChild | WsVisible | WsTabStop | CbsDropDownList, 20, 308, 170, 120, hwnd, (nint)IdProviderCombo, instance, 0);

        CreateLabel(hwnd, instance, "Chave de API (Cloud):", 200, 285, 180, 20);
        hApiKeyEdit = CreateWindowEx(0, "EDIT", "", WsChild | WsVisible | WsTabStop | WsBorder | EsAutoHScroll | EsPassword, 200, 308, 180, 24, hwnd, (nint)IdApiKeyEdit, instance, 0);

        // Buttons
        _ = CreateWindowEx(0, "BUTTON", "Salvar", WsChild | WsVisible | WsTabStop | BsDefPushButton, 190, 410, 90, 30, hwnd, (nint)IdSaveButton, instance, 0);
        _ = CreateWindowEx(0, "BUTTON", "Cancelar", WsChild | WsVisible | WsTabStop | BsPushButton, 290, 410, 90, 30, hwnd, (nint)IdCancelButton, instance, 0);

        // Apply Segoe UI font to all child controls
        SetControlFont(hDeviceCombo);
        SetControlFont(hHotkeyLabel);
        SetControlFont(hAutoPasteCheck);
        SetControlFont(hSoundCuesCheck);
        SetControlFont(hStartWithWindowsCheck);
        SetControlFont(hVocabEdit);
        SetControlFont(hProviderCombo);
        SetControlFont(hApiKeyEdit);
    }

    private void CreateLabel(nint parent, nint instance, string text, int x, int y, int w, int h)
    {
        var hLbl = CreateWindowEx(0, "STATIC", text, WsChild | WsVisible, x, y, w, h, parent, 0, instance, 0);
        SetControlFont(hLbl);
    }

    private void SetControlFont(nint controlHwnd)
    {
        if (controlHwnd != 0 && hFont != 0)
        {
            SendMessageW(controlHwnd, WmSetFont, hFont, 1);
        }
    }

    private void PopulateControls()
    {
        var settings = pendingSettings ?? new UserSettings();

        // 1. Devices ComboBox
        cachedDevices = AudioDeviceEnumerator.GetCaptureDevices();
        SendMessageW(hDeviceCombo, 0x014B /* CB_RESETCONTENT */, 0, 0);
        SendMessageW(hDeviceCombo, CbAddString, 0, "Padrão do Windows");

        int selectedDeviceIdx = 0;
        for (int i = 0; i < cachedDevices.Count; i++)
        {
            var d = cachedDevices[i];
            SendMessageW(hDeviceCombo, CbAddString, 0, d.Name);
            if (!string.IsNullOrEmpty(settings.MicrophoneDeviceId) &&
                string.Equals(d.Id, settings.MicrophoneDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                selectedDeviceIdx = i + 1;
            }
        }
        SendMessageW(hDeviceCombo, CbSetCurSel, (nint)selectedDeviceIdx, 0);

        // 2. Checkboxes
        SendMessageW(hAutoPasteCheck, BmSetCheck, (nint)(settings.AutoPaste ? 1 : 0), 0);
        SendMessageW(hSoundCuesCheck, BmSetCheck, (nint)(settings.PlaySoundCues ? 1 : 0), 0);
        SendMessageW(hStartWithWindowsCheck, BmSetCheck, (nint)(settings.StartWithWindows ? 1 : 0), 0);

        // 3. Vocab
        SetWindowTextW(hVocabEdit, settings.CustomVocabulary ?? string.Empty);

        // 4. Provider
        SendMessageW(hProviderCombo, 0x014B /* CB_RESETCONTENT */, 0, 0);
        SendMessageW(hProviderCombo, CbAddString, 0, "Local (Whisper)");
        SendMessageW(hProviderCombo, CbAddString, 0, "Groq Cloud");
        SendMessageW(hProviderCombo, CbAddString, 0, "OpenAI Cloud");

        int providerIdx = settings.CloudProvider?.ToLowerInvariant() switch
        {
            "groq" => 1,
            "openai" => 2,
            _ => 0
        };
        SendMessageW(hProviderCombo, CbSetCurSel, (nint)providerIdx, 0);

        // 5. API Key (if encrypted secret exists, show placeholder mask)
        SetWindowTextW(hApiKeyEdit, !string.IsNullOrEmpty(settings.EncryptedApiKey) ? "********" : "");
    }

    private void OnSaveClicked()
    {
        var settings = pendingSettings ?? new UserSettings();

        // 1. Device
        var selIdx = (int)SendMessageW(hDeviceCombo, CbGetCurSel, 0, 0);
        string? deviceId = null;
        if (selIdx > 0 && selIdx - 1 < cachedDevices.Count)
        {
            deviceId = cachedDevices[selIdx - 1].Id;
        }

        // 2. Checkboxes
        bool autoPaste = SendMessageW(hAutoPasteCheck, BmGetCheck, 0, 0) == 1;
        bool soundCues = SendMessageW(hSoundCuesCheck, BmGetCheck, 0, 0) == 1;
        bool startWithWindows = SendMessageW(hStartWithWindowsCheck, BmGetCheck, 0, 0) == 1;

        // 3. Vocab
        string vocab = GetWindowText(hVocabEdit);

        // 4. Provider
        var provIdx = (int)SendMessageW(hProviderCombo, CbGetCurSel, 0, 0);
        string provider = provIdx switch
        {
            1 => "groq",
            2 => "openai",
            _ => "none"
        };

        // 5. API Key
        string apiKeyInput = GetWindowText(hApiKeyEdit);
        string? encryptedKey = settings.EncryptedApiKey;
        if (!string.IsNullOrEmpty(apiKeyInput) && apiKeyInput != "********")
        {
            encryptedKey = protector.Protect(apiKeyInput);
        }
        else if (string.IsNullOrEmpty(apiKeyInput))
        {
            encryptedKey = null;
        }

        var updated = settings with
        {
            MicrophoneDeviceId = deviceId,
            AutoPaste = autoPaste,
            PlaySoundCues = soundCues,
            StartWithWindows = startWithWindows,
            CustomVocabulary = vocab.Trim(),
            CloudProvider = provider,
            EncryptedApiKey = encryptedKey
        };

        pendingOnSave?.Invoke(updated);
        Close();
    }

    private nint WndProc(nint window, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WmCommand:
                int wmId = (int)(wParam & 0xFFFF);
                if (wmId == IdSaveButton)
                {
                    OnSaveClicked();
                    return 0;
                }
                else if (wmId == IdCancelButton)
                {
                    Close();
                    return 0;
                }
                break;
            case WmClose:
                Close();
                return 0;
            case 0x0002: // WM_DESTROY
                return 0;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }

    private static string GetWindowText(nint hWnd)
    {
        if (hWnd == 0) return string.Empty;
        var len = (int)SendMessageW(hWnd, 0x000E /* WM_GETTEXTLENGTH */, 0, 0);
        if (len <= 0) return string.Empty;

        var buffer = new char[len + 1];
        unsafe
        {
            fixed (char* pBuf = buffer)
            {
                SendMessageW(hWnd, 0x000D /* WM_GETTEXT */, (nint)(len + 1), (nint)pBuf);
            }
        }
        return new string(buffer, 0, len);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (threadId != 0)
        {
            PostThreadMessageW(threadId, WmAppQuit, 0, 0);
        }

        staThread.Join(1000);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Hwnd;
        public uint Msg;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(in WindowClass windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Message message, nint window, uint filterMin, uint filterMax);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(in Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(in Message message);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] private static extern bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)] private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, string lParam);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(nint hWnd, string lpString);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision,
        uint iQuality, uint iPitchAndFamily, string pszFaceName);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint hObject);
}
