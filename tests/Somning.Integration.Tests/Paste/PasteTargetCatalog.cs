using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Somning.Integration.Tests.Paste;

/// <summary>A real application the paste matrix drives (Chrome, Edge, VS Code, Word).</summary>
public sealed record PasteTargetApp(string Id, string DisplayName, string ExeName, IReadOnlyList<string> FallbackPaths)
{
    public bool RequiresNoRunningInstance => Id == "word";
}

/// <summary>
/// Catalog of paste-matrix targets. Everything launched uses temp profile/extension dirs only (T-04-05); windows are found by
/// a unique marker in the title (multi-process apps make MainWindowHandle of the launcher unreliable).
/// </summary>
public static class PasteTargetCatalog
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly PasteTargetApp[] Targets =
    [
        new("chrome", "Google Chrome", "chrome.exe",
            [Path.Combine(ProgramFiles, "Google", "Chrome", "Application", "chrome.exe"),
             Path.Combine(ProgramFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
             Path.Combine(LocalAppData, "Google", "Chrome", "Application", "chrome.exe")]),
        new("edge", "Microsoft Edge", "msedge.exe",
            [Path.Combine(ProgramFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
             Path.Combine(ProgramFiles, "Microsoft", "Edge", "Application", "msedge.exe")]),
        new("vscode", "Visual Studio Code", "Code.exe",
            [Path.Combine(LocalAppData, "Programs", "Microsoft VS Code", "Code.exe"),
             Path.Combine(ProgramFiles, "Microsoft VS Code", "Code.exe")]),
        new("word", "Microsoft Word", "winword.exe",
            [Path.Combine(ProgramFiles, "Microsoft Office", "root", "Office16", "WINWORD.EXE"),
             Path.Combine(ProgramFilesX86, "Microsoft Office", "root", "Office16", "WINWORD.EXE"),
             Path.Combine(ProgramFiles, "Microsoft Office", "Office16", "WINWORD.EXE")])
    ];

    public static IReadOnlyList<string> Ids { get; } = Targets.Select(t => t.Id).ToArray();

    public static PasteTargetApp Get(string id) =>
        Targets.SingleOrDefault(t => t.Id == id) ?? throw new ArgumentException("Unknown paste target " + id, nameof(id));

    public static bool IsInstalled(string id) => ResolveExe(Get(id)) is not null;

    public static string? ResolveExe(PasteTargetApp app)
    {
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + app.ExeName);
            if (key?.GetValue(null) is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
        }
        return app.FallbackPaths.FirstOrDefault(File.Exists);
    }

    /// <summary>True when an instance of the app already runs (the matrix refuses to touch the owner's Word instance).</summary>
    public static bool IsAlreadyRunning(PasteTargetApp app)
    {
        var name = Path.GetFileNameWithoutExtension(app.ExeName);
        foreach (var process in Process.GetProcesses())
        {
            try { if (process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)) return true; }
            finally { process.Dispose(); }
        }
        return false;
    }

    /// <summary>Starts the app with isolated temp state and a window whose title carries <paramref name="marker"/>.</summary>
    public static Process Launch(PasteTargetApp app, string exe, string tempDir, string marker)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var argument in Arguments(app, tempDir, marker)) info.ArgumentList.Add(argument);
        return Process.Start(info) ?? throw new IOException(app.DisplayName + " did not start.");
    }

    private static IEnumerable<string> Arguments(PasteTargetApp app, string tempDir, string marker)
    {
        var profile = Path.Combine(tempDir, "profile");
        switch (app.Id)
        {
            case "chrome":
            case "edge":
                return ["--user-data-dir=" + profile, "--no-first-run", "--no-default-browser-check", "--new-window",
                    "data:text/html,<title>" + marker + "</title><textarea%20autofocus%20rows=10%20cols=60></textarea>"];
            case "vscode":
                var file = Path.Combine(tempDir, marker + ".txt");
                File.WriteAllText(file, "");
                // A fresh profile opens a modal "Welcome / sign in" dialog that takes keyboard focus and swallows the paste;
                // pre-seed the temp profile so the editor is the focused control.
                var userDir = Path.Combine(profile, "User");
                Directory.CreateDirectory(userDir);
                File.WriteAllText(Path.Combine(userDir, "settings.json"),
                    "{\"workbench.startupEditor\":\"none\",\"chat.disableAIFeatures\":true,\"telemetry.telemetryLevel\":\"off\"," +
                    "\"window.restoreWindows\":\"none\",\"security.workspace.trust.enabled\":false,\"update.mode\":\"none\"}");
                return ["--new-window", "--user-data-dir", profile, "--extensions-dir", Path.Combine(tempDir, "extensions"),
                    "--disable-extensions", "--skip-welcome", "--skip-release-notes", "--disable-workspace-trust", file];
            case "word":
                var document = Path.Combine(tempDir, marker + ".docx");
                WriteBlankDocx(document);
                return ["/q", "/n", document];
            default:
                throw new ArgumentException("Unknown paste target " + app.Id);
        }
    }

    private static void WriteBlankDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(archive, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/></Types>");
        Add(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add(archive, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/></Relationships>");
        // compatibilityMode 15 avoids Word's "Compatibility Mode" (legacy) opening path.
        Add(archive, "word/settings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
        Add(archive, "word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p/></w:body></w:document>");
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Finds a visible top-level window whose title contains the marker; 0 when none appears in time.</summary>
    public static async Task<nint> FindWindowAsync(string marker, TimeSpan timeout, CancellationToken token)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var found = FindWindowNow(marker);
            if (found != 0) return found;
            await Task.Delay(250, token);
        }
        return 0;
    }

    private static nint FindWindowNow(string marker)
    {
        nint result = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            var length = GetWindowTextLength(window);
            if (length <= 0) return true;
            var text = new StringBuilder(length + 1);
            _ = GetWindowText(window, text, text.Capacity);
            if (!text.ToString().Contains(marker, StringComparison.Ordinal)) return true;
            result = window;
            return false;
        }, 0);
        return result;
    }

    /// <summary>
    /// Window class of the control that must hold the keyboard focus for a paste into the document to be meaningful:
    /// the web-contents widget for Chromium apps (not the address bar or a dialog) and the Word document body.
    /// </summary>
    public static string ExpectedEditorClass(string id) => id switch
    {
        "chrome" or "edge" or "vscode" => "Chrome_RenderWidgetHostHWND",
        "word" => "_WwG",
        _ => throw new ArgumentException("Unknown paste target " + id, nameof(id))
    };

    public static string WindowClass(nint window)
    {
        var text = new StringBuilder(256);
        return window == 0 || GetClassName(window, text, text.Capacity) == 0 ? string.Empty : text.ToString();
    }

    public static uint WindowProcessId(nint window)
    {
        _ = GetWindowThreadProcessId(window, out var pid);
        return pid;
    }

    /// <summary>Kills the process trees this test started (launcher and the window owner), by PID only.</summary>
    public static void KillStarted(Process launched, uint windowOwnerPid)
    {
        TryKillTree(launched);
        if (windowOwnerPid == 0 || windowOwnerPid == launched.Id) return;
        try
        {
            using var owner = Process.GetProcessById((int)windowOwnerPid);
            TryKillTree(owner);
        }
        catch (ArgumentException) { }
    }

    /// <summary>
    /// Kills EVERY process whose command line mentions the temp profile directory name (launcher, browser/editor children,
    /// crashpad, GPU, renderer...). Multi-process apps detach children from the launcher, so a PID-based kill leaves them
    /// alive holding inherited stdio handles, which hangs any runner waiting on the redirected output.
    /// </summary>
    public static void KillTreesUsingProfile(string tempDir)
    {
        var token = Path.GetFileName(tempDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(token)) return;
        const string Script =
            "$d=$env:SOMNING_KILL_TOKEN; Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -and " +
            "$_.CommandLine.IndexOf($d,[StringComparison]::OrdinalIgnoreCase) -ge 0 } | " +
            "ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }";
        var info = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-Command"); info.ArgumentList.Add(Script);
        info.Environment["SOMNING_KILL_TOKEN"] = token;
        try
        {
            using var killer = Process.Start(info);
            if (killer is null) return;
            _ = killer.StandardOutput.ReadToEndAsync();
            _ = killer.StandardError.ReadToEndAsync();
            if (!killer.WaitForExit(20000)) killer.Kill(entireProcessTree: true);
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    private static void TryKillTree(Process process)
    {
        try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); } }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassName(nint window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")] private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
