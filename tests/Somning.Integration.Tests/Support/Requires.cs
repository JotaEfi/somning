using Somning.Core.Engine;
using Somning.Core.Paths;
using Xunit;
namespace Somning.Integration.Tests.Support;
public static class Requires
{
    public static void That(bool condition, string reason)
    {
        if (condition) return;
        if (Environment.GetEnvironmentVariable("SOMNING_STRICT") == "1") Assert.Fail(reason);
        Assert.Skip(reason);
    }
    public static void Models() => That(File.Exists(Path.Combine(SomningPaths.ModelsDir, EngineDefaults.ModelFile))
        && File.Exists(Path.Combine(SomningPaths.ModelsDir, EngineDefaults.VadModelFile)), "Whisper and VAD models required.");
    public static void Desktop() => That(Environment.UserInteractive, "Interactive desktop required.");
}
