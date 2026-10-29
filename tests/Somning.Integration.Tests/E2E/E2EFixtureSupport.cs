using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave;
using Somning.Core.Engine;
using Somning.Core.Text;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Clipboard;
using Xunit;

namespace Somning.Integration.Tests.E2E;

internal sealed class E2EFixtureSupport : IDisposable
{
    private E2EFixtureSupport(AppHarnessDriver app, NotepadDriver notepad, nint layout)
    { App = app; Notepad = notepad; LayoutBefore = layout; }

    public AppHarnessDriver App { get; }
    public NotepadDriver Notepad { get; }
    public nint LayoutBefore { get; }
    public static string Fixture(string name) => Path.Combine(RepoPaths.Fixtures, name.Replace('/', Path.DirectorySeparatorChar));
    public static string Expected(string name)
    {
        var relative = Path.GetRelativePath(RepoPaths.Fixtures, Fixture(Path.ChangeExtension(name, ".wav"))).Replace(Path.DirectorySeparatorChar, '/');
        var row = Manifest().Single(x => x.GetProperty("file").GetString() == relative);
        return row.GetProperty("expected").GetString()!;
    }
    public static TimeSpan Duration(string fixture) { using var reader = new WaveFileReader(fixture); return reader.TotalTime; }
    public static nint CurrentLayout(nint window)
    {
        var thread = GetWindowThreadProcessId(window, out _);
        return GetKeyboardLayout(thread);
    }

    public static async Task<E2EFixtureSupport> StartAsync(string fixture, IReadOnlyDictionary<string, string>? env = null,
        string? dataDir = null, bool waitForEngine = true)
    {
        Requires.Models(); Requires.Desktop();
        var app = await AppHarnessDriver.StartAsync(fixture, env, waitForEngine, dataDir);
        try
        {
            await app.WaitEventAsync("recovery_complete", 0, null, TimeSpan.FromSeconds(30));
            var notepad = NotepadDriver.Start();
            if (!ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit))
            { notepad.Dispose(); throw new InvalidOperationException("test-owned Notepad could not be brought to foreground"); }
            notepad.SetText(string.Empty);
            return new E2EFixtureSupport(app, notepad, CurrentLayout(notepad.Window));
        }
        catch { app.Dispose(); throw; }
    }

    public async Task<HarnessEvent> DictateAsync(string fixture, bool hold, long afterSeq = -1, TimeSpan? holdFor = null)
    {
        Assert.True(ForegroundHelper.EnsureForeground(Notepad.Window, Notepad.Edit), "test-owned Notepad must be foreground before hotkey injection");
        await App.SetFixtureAsync(fixture);
        if (afterSeq < 0) afterSeq = await App.LastSeqAsync();
        if (hold)
        {
            HotkeyInjector.Down();
            try { await Task.Delay(holdFor ?? Duration(fixture), TestContext.Current.CancellationToken); }
            finally { HotkeyInjector.Up(); }
        }
        else
        {
            HotkeyInjector.Chord(70);
            await Task.Delay(Duration(fixture) + TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);
            Assert.True(ForegroundHelper.EnsureForeground(Notepad.Window, Notepad.Edit), "test-owned Notepad must be foreground before stop hotkey");
            HotkeyInjector.Chord(70);
        }
        var target = await App.WaitEventAsync("target_captured", afterSeq, null, TimeSpan.FromSeconds(20));
        return await App.WaitEventAsync("outcome", target.Seq - 1, target.Id, TimeSpan.FromMinutes(2));
    }

    public async Task<HarnessEvent> StartAndStopAsync(string fixture, int captureMs, long afterSeq)
    {
        Assert.True(ForegroundHelper.EnsureForeground(Notepad.Window, Notepad.Edit));
        await App.SetFixtureAsync(fixture);
        HotkeyInjector.Chord(70);
        await Task.Delay(captureMs, TestContext.Current.CancellationToken);
        Assert.True(ForegroundHelper.EnsureForeground(Notepad.Window, Notepad.Edit));
        HotkeyInjector.Chord(70);
        var target = await App.WaitEventAsync("target_captured", afterSeq, null, TimeSpan.FromSeconds(20));
        return await App.WaitEventAsync("outcome", target.Seq - 1, target.Id, TimeSpan.FromMinutes(2));
    }

    public void AssertPasted(HarnessEvent outcome, string expected, bool accuracy = true)
    {
        Assert.Equal("pasted", outcome.Outcome);
        var text = Notepad.WaitForText(ClipboardProbe.ReadText(), TimeSpan.FromSeconds(3));
        Assert.NotEmpty(text);
        var clipboardText = ClipboardProbe.ReadText();
        Assert.True(text == clipboardText,
            $"Notepad text differs from the clipboard (a leading space here means a hotkey Space leaked into Notepad): notepad='{Escape(text)}' clipboard='{Escape(clipboardText)}'");
        Assert.True(text == text.Trim(), $"Pasted text has leading/trailing whitespace: '{Escape(text)}'");
        Assert.DoesNotContain('\n', text); Assert.DoesNotContain('\r', text);
        if (accuracy) Assert.True(Wer.Compute(expected, text) <= 0.10, $"WER exceeded 0.10: {text}");
    }

    private static string Escape(string value) => value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    public void AssertLayoutUnchanged() => Assert.Equal(LayoutBefore, CurrentLayout(Notepad.Window));
    public void Dispose() { HotkeyInjector.ReleaseAll(); Notepad.Dispose(); App.Dispose(); }

    public static JsonElement[] Manifest() => JsonDocument.Parse(File.ReadAllText(Fixture("manifest.json")))
        .RootElement.EnumerateArray().Select(x => x.Clone()).ToArray();

    public static string[] EnginePids(int parentPid) => Process.GetProcesses().Where(p =>
    {
        try { return p.ProcessName.Equals("Somning.Engine", StringComparison.OrdinalIgnoreCase) &&
            ParentPid(p.Id) == parentPid; }
        catch { return false; }
    }).Select(p => { var id = p.Id.ToString(); p.Dispose(); return id; }).ToArray();

    public static int ParentPid(int pid)
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1)) return -1;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (Process32First(snapshot, ref entry))
                do { if (entry.ProcessId == pid) return checked((int)entry.ParentProcessId); }
                while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }
        return -1;
    }

    public static void SetClipboard(string text)
    {
        using var clipboard = new ClipboardWriter();
        var result = clipboard.SetText(text);
        if (!result.Success) throw new IOException("Could not set sentinel clipboard text.");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
}
