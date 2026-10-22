using Somning.Core.Paths;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace Somning.Engine;
public static class PathGuard
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    public static FileStream? OpenValidated(string root, string path)
    {
        FileStream? stream = null;
        try
        {
            if (!Accepts(root, path)) return null;
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var final = new StringBuilder(32768);
            var count = GetFinalPathNameByHandle(stream.SafeFileHandle, final, (uint)final.Capacity, 0);
            var resolved = final.ToString();
            if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
            else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
            if (count == 0 || count >= final.Capacity || !Accepts(root, path)
                || !PathContainment.IsStrictlyUnder(root, resolved)
                || !string.Equals(Path.GetFullPath(path), resolved, StringComparison.OrdinalIgnoreCase))
            { stream.Dispose(); return null; }
            return stream;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { stream?.Dispose(); return null; }
    }
    public static bool Accepts(string root, string path)
    {
        try
        {
            if (!PathContainment.IsStrictlyUnder(root, path)
                || !string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path)) return false;
            FileSystemInfo? item = new FileInfo(Path.GetFullPath(path));
            while (item is not null)
            {
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                item = item is FileInfo f ? f.Directory : ((DirectoryInfo)item).Parent;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException) { return false; }
    }
}
