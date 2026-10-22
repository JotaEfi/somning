namespace Somning.Core.Paths;
public static class SomningPaths
{
    public static string DataRoot => Environment.GetEnvironmentVariable("SOMNING_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Somning");
    public static string ModelsDir => Environment.GetEnvironmentVariable("SOMNING_MODELS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Somning", "models");
    public static string RecordingsDir => Path.Combine(DataRoot, "recordings");
    public static string LogsDir => Path.Combine(DataRoot, "logs");
}
