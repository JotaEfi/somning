using System.Diagnostics;
using Somning.Integration.Tests.E2E;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Clipboard;
using Somning.Platform.Windows.Paste;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Xunit;
using PInvoke = Windows.Win32.IntegrationPInvoke;

namespace Somning.Integration.Tests.Paste;

/// <summary>
/// DIST-03 paste matrix: real TargetPaster into Chrome, Edge, VS Code and Word, verified by a sentinel-clipboard
/// Ctrl+A / Ctrl+C read-back. Missing apps are SKIPPED (never passed); a read-back mismatch is a FAIL.
/// </summary>
[Trait("Category", "Desktop")]
public sealed class PasteTargetMatrixTests
{
    private const string Sentinel = "SOMNING-SENTINEL-NAO-COLAR";
    private const string PastedText = "Olá, ação às 10h: coração, não é São João.";
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReadBackTimeout = TimeSpan.FromSeconds(3);

    public static TheoryData<string> TargetIds => new(PasteTargetCatalog.Ids);

    [Theory]
    [MemberData(nameof(TargetIds))]
    public async Task Pastes_into_target_application(string id)
    {
        Requires.Desktop();
        var app = PasteTargetCatalog.Get(id);
        var exe = PasteTargetCatalog.ResolveExe(app);
        if (exe is null) Assert.Skip(app.DisplayName + " not installed on this machine");
        if (app.RequiresNoRunningInstance && PasteTargetCatalog.IsAlreadyRunning(app))
            Assert.Skip(app.DisplayName + " is already running; refusing to touch the owner's instance");

        var token = TestContext.Current.CancellationToken;
        var marker = "somning" + Guid.NewGuid().ToString("N")[..8];
        var tempDir = Path.Combine(Path.GetTempPath(), "somning-paste-" + marker);
        Directory.CreateDirectory(tempDir);
        Process? launched = null;
        uint ownerPid = 0;
        try
        {
            launched = PasteTargetCatalog.Launch(app, exe, tempDir, marker);
            var window = await PasteTargetCatalog.FindWindowAsync(marker, WindowTimeout, token);
            Assert.True(window != 0, $"{app.DisplayName} window with marker {marker} did not appear");
            ownerPid = PasteTargetCatalog.WindowProcessId(window);
            await Task.Delay(TimeSpan.FromSeconds(2), token); // let the editor/page take initial focus

            Requires.That(ForegroundHelper.EnsureForeground(window, 0), $"could not bring {app.DisplayName} to foreground");
            await Task.Delay(TimeSpan.FromSeconds(1), token);

            using var clipboard = new ClipboardWriter();
            var integrity = new IntegrityReader();
            var probe = new ForegroundProbe(integrity);
            var target = probe.Capture();
            var paster = new TargetPaster(clipboard, probe, integrity, new ModifierWaiter(), new PasteInjector());

            var result = await paster.DeliverAsync(PastedText, target, token);
            Assert.Equal("pasted", result.Outcome);

            var readBack = await ReadBackAsync(token);
            // Word appends a paragraph mark (and cell/bell characters) to the copied text.
            Assert.Equal(PastedText, readBack.TrimEnd('\r', '\n', '\a'));
        }
        finally
        {
            if (launched is not null) PasteTargetCatalog.KillStarted(launched, ownerPid);
            launched?.Dispose();
            DeleteWithRetries(tempDir);
        }
    }

    /// <summary>Sentinel clipboard, then Ctrl+A and Ctrl+C in the focused control; returns what the app copied.</summary>
    private static async Task<string> ReadBackAsync(CancellationToken token)
    {
        await Task.Delay(500, token);
        E2EFixtureSupport.SetClipboard(Sentinel);
        SendCtrlChord(VIRTUAL_KEY.VK_A);
        await Task.Delay(150, token);
        SendCtrlChord(VIRTUAL_KEY.VK_C);
        var until = DateTime.UtcNow + ReadBackTimeout;
        var text = Sentinel;
        while (DateTime.UtcNow < until)
        {
            text = ClipboardProbe.ReadText();
            if (text != Sentinel) break;
            await Task.Delay(50, token);
        }
        return text;
    }

    private static unsafe void SendCtrlChord(VIRTUAL_KEY key)
    {
        var inputs = new INPUT[4];
        (VIRTUAL_KEY Key, bool Down)[] steps =
            [(VIRTUAL_KEY.VK_CONTROL, true), (key, true), (key, false), (VIRTUAL_KEY.VK_CONTROL, false)];
        for (var i = 0; i < steps.Length; i++)
        {
            inputs[i].type = INPUT_TYPE.INPUT_KEYBOARD;
            inputs[i].ki.wVk = steps[i].Key;
            inputs[i].ki.dwFlags = steps[i].Down ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }
        var sent = PInvoke.SendInput(inputs, sizeof(INPUT));
        Requires.That(sent == inputs.Length, "SendInput refused the read-back chord (UIPI or blocked input)");
    }

    private static void DeleteWithRetries(string path)
    {
        for (var attempt = 0; attempt < 10 && Directory.Exists(path); attempt++)
        {
            try { Directory.Delete(path, recursive: true); }
            catch (IOException) { Thread.Sleep(300); }
            catch (UnauthorizedAccessException) { Thread.Sleep(300); }
        }
    }
}
