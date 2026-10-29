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
    public static string AppExe
    {
        get
        {
            // SOMNING_APP_EXE targets an installed copy (e.g. a harness-enabled package) instead of the Debug build.
            var overridePath = Environment.GetEnvironmentVariable("SOMNING_APP_EXE");
            if (string.IsNullOrWhiteSpace(overridePath))
                return Path.Combine(Root, "src", "Somning.App", "bin", "Debug", "net10.0-windows", "Somning.App.exe");
            if (!File.Exists(overridePath))
                throw new FileNotFoundException("SOMNING_APP_EXE points to a file that does not exist.", overridePath);
            return overridePath;
        }
    }
}
