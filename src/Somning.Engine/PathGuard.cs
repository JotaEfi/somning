using Somning.Core.Paths;
namespace Somning.Engine;
public static class PathGuard
{
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
