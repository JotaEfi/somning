namespace Somning.Core.Paths;
public static class PathContainment
{
    public static bool IsStrictlyUnder(string root, string candidate)
    {
        try
        {
            var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(candidate);
            return full.Length > prefix.Length && full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        { return false; }
    }
}
