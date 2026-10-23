using System.Globalization;
using Serilog;
using Somning.Core.Paths;
namespace Somning.App.Logging;
public static class LoggingSetup
{
    public const string FileOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}";

    public static void Initialize() => Initialize(SomningPaths.LogsDir);

    public static void Initialize(string logsDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDir);
        Directory.CreateDirectory(logsDir);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .Destructure.With(new RedactionPolicy())
            .WriteTo.File(Path.Combine(logsDir, "somning-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7, outputTemplate: FileOutputTemplate,
                formatProvider: CultureInfo.InvariantCulture).CreateLogger();
    }
}
