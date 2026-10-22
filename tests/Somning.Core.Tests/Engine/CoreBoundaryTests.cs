using Somning.Core.Engine;
using Xunit;
namespace Somning.Core.Tests.Engine;
public sealed class CoreBoundaryTests
{
    [Fact]
    public void Core_and_unit_tests_do_not_reference_App_or_Platform()
    {
        foreach (var assembly in new[] { typeof(EngineTranscription).Assembly, typeof(CoreBoundaryTests).Assembly })
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), referenced =>
                referenced.Name!.StartsWith("Somning.App", StringComparison.Ordinal)
                || referenced.Name.StartsWith("Somning.Platform", StringComparison.Ordinal));
    }
}
