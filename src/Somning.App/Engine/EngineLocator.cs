namespace Somning.App.Engine;
public static class EngineLocator
{
    public static string Find(string? explicitPath = null)
    {
        var configured = explicitPath ?? Environment.GetEnvironmentVariable("SOMNING_ENGINE_PATH");
        if (configured is not null) return Path.GetFullPath(configured);
        var packaged = Path.Combine(AppContext.BaseDirectory, "engine", "Somning.Engine.exe");
        if (File.Exists(packaged)) return packaged;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Somning.sln"))) dir = dir.Parent;
        if (dir is null) throw new FileNotFoundException("Engine executable not found.");
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        return Path.Combine(dir.FullName, "src", "Somning.Engine", "bin", configuration, "net10.0-windows", "Somning.Engine.exe");
    }
}
