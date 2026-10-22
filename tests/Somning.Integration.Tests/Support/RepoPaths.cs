namespace Somning.Integration.Tests.Support;
public static class RepoPaths
{
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Somning.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Somning.sln not found.");
        }
    }
    public static string Fixtures => Path.Combine(Root, "tests", "fixtures");
    public static string EngineExe => Path.Combine(Root, "src", "Somning.Engine", "bin", "Debug",
        "net10.0-windows", "Somning.Engine.exe");
}
