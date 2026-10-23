using Somning.Core.Output;
using Somning.Core.Ports;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Somning.Platform.Windows.Paste;

public interface IIntegrityReader
{
    int? ReadRid(uint processId);
    int OwnRid();
}

public sealed class IntegrityReader : IIntegrityReader
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenIntegrityLevel = 25;
    private int? ownRid;

    public int OwnRid()
    {
        if (ownRid is int cached) return cached;
        var process = Native.GetCurrentProcess();
        if (!Native.OpenProcessToken(process, TokenQuery, out var token))
            throw new InvalidOperationException("own_integrity_unavailable");
        using (token)
        {
            var rid = ReadTokenRid(token) ?? throw new InvalidOperationException("own_integrity_unavailable");
            ownRid = rid;
            return rid;
        }
    }

    public int? ReadRid(uint processId)
    {
        var process = Native.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid) { process.Dispose(); return null; }
        using (process)
        {
            if (!Native.OpenProcessToken(process, TokenQuery, out var token)) return null;
            using (token) return ReadTokenRid(token);
        }
    }

    private static int? ReadTokenRid(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token)
    {
        _ = Native.GetTokenInformation(token, TokenIntegrityLevel, 0, 0, out var size);
        if (size == 0) return null;
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(checked((int)size));
        try
        {
            if (!Native.GetTokenInformation(token, TokenIntegrityLevel, buffer, size, out _)) return null;
            var label = System.Runtime.InteropServices.Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            if (label.SidAndAttributes.Sid == 0) return null;
            var countPointer = Native.GetSidSubAuthorityCount(label.SidAndAttributes.Sid);
            if (countPointer == 0) return null;
            var count = System.Runtime.InteropServices.Marshal.ReadByte(countPointer);
            if (count == 0) return null;
            var ridPointer = Native.GetSidSubAuthority(label.SidAndAttributes.Sid, (uint)(count - 1));
            return ridPointer == 0 ? null : System.Runtime.InteropServices.Marshal.ReadInt32(ridPointer);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SidAndAttributes { public nint Sid; public uint Attributes; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct TokenMandatoryLabel { public SidAndAttributes SidAndAttributes; }

    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern nint GetCurrentProcess();
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool OpenProcessToken(Microsoft.Win32.SafeHandles.SafeProcessHandle process, uint access,
            out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, EntryPoint = "OpenProcessToken")]
        public static extern bool OpenProcessToken(nint process, uint access,
            out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle token,
            int informationClass, nint information, uint length, out uint returnLength);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        public static extern nint GetSidSubAuthorityCount(nint sid);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        public static extern nint GetSidSubAuthority(nint sid, uint index);
    }
}

public sealed class ForegroundProbe(IIntegrityReader integrityReader) : ITargetProbe
{
    public unsafe PasteTarget Capture()
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
            return new(0, 0, 0, 0, false, null);

        var threadId = PInvoke.GetWindowThreadProcessId(foreground, out var processId);
        if (threadId == 0 || processId == 0)
            return new((nint)foreground.Value, 0, processId, threadId, false, null);

        var info = new GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        var queried = PInvoke.GetGUIThreadInfo(threadId, ref info);
        return new((nint)foreground.Value, queried ? (nint)info.hwndFocus.Value : 0,
            processId, threadId, queried, integrityReader.ReadRid(processId));
    }
}
