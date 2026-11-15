using Somning.Core.Engine;
using Xunit;

namespace Somning.Core.Tests.Engine;

public sealed class EngineModelSelectionTests : IDisposable
{
    private readonly string modelsDir = Path.Combine(Path.GetTempPath(), "somning-models-" + Guid.NewGuid().ToString("N"));

    public EngineModelSelectionTests() => Directory.CreateDirectory(modelsDir);

    public void Dispose() => Directory.Delete(modelsDir, recursive: true);

    [Fact]
    public void Cpu_backend_uses_q8_model_when_it_is_installed()
    {
        File.WriteAllText(Path.Combine(modelsDir, EngineDefaults.CpuFallbackModelFile), "");

        Assert.Equal(EngineDefaults.CpuFallbackModelFile, EngineDefaults.ModelFileFor("cpu", modelsDir));
    }

    [Fact]
    public void Cpu_backend_falls_back_to_required_q5_model_when_q8_is_not_installed()
    {
        File.WriteAllText(Path.Combine(modelsDir, EngineDefaults.ModelFile), "");

        Assert.Equal(EngineDefaults.ModelFile, EngineDefaults.ModelFileFor("cpu", modelsDir));
    }

    [Fact]
    public void Vulkan_backend_always_uses_required_q5_model()
    {
        File.WriteAllText(Path.Combine(modelsDir, EngineDefaults.CpuFallbackModelFile), "");

        Assert.Equal(EngineDefaults.ModelFile, EngineDefaults.ModelFileFor("vulkan", modelsDir));
    }
}
