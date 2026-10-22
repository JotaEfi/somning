using Serilog;
using Somning.App.Logging;
using Somning.App.Modes;
using Somning.Core.Engine;
LoggingSetup.Initialize();
try
{
    if (args.Length < 2 || args[0] != "--transcribe-file" || args.Length % 2 != 0)
        throw new ArgumentException("Expected --transcribe-file <wav> [--backend cpu|vulkan] [--engine path].");
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (args[i] is not ("--transcribe-file" or "--backend" or "--engine") || !options.TryAdd(args[i], args[i + 1]))
            throw new ArgumentException("Unknown or duplicate argument.");
    }
    return await TranscribeFileCommand.RunAsync(options["--transcribe-file"],
        options.GetValueOrDefault("--backend", EngineDefaults.Backend), options.GetValueOrDefault("--engine"));
}
catch (Exception)
{
    Log.Error("App command failed");
    return 1;
}
finally { Log.CloseAndFlush(); }
