using System.Globalization;
using Serilog;
using Somning.Core.Paths;
namespace Somning.App.Logging;
public static class LoggingSetup
{
    public static void Initialize()
    {
        Directory.CreateDirectory(SomningPaths.LogsDir);
        Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
            .WriteTo.File(Path.Combine(SomningPaths.LogsDir, "somning-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7, formatProvider: CultureInfo.InvariantCulture).CreateLogger();
    }
}
