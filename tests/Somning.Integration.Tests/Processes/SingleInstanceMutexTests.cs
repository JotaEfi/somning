using Somning.Platform.Windows.Processes;
using Xunit;

namespace Somning.Integration.Tests.Processes;

public sealed class SingleInstanceMutexTests
{
    [Fact]
    public void FirstInstance_AcquiresSuccessfully_SecondInstanceFails()
    {
        var uniqueName = @"Local\Somning_Test_Mutex_" + Guid.NewGuid().ToString("N");

        using var first = new SingleInstanceMutex(uniqueName);
        Assert.True(first.IsFirstInstance);

        using var second = new SingleInstanceMutex(uniqueName);
        Assert.False(second.IsFirstInstance);
    }

    [Fact]
    public void AfterFirstDisposed_SubsequentInstance_CanAcquire()
    {
        var uniqueName = @"Local\Somning_Test_Mutex_" + Guid.NewGuid().ToString("N");

        using (var first = new SingleInstanceMutex(uniqueName))
        {
            Assert.True(first.IsFirstInstance);
        }

        using var second = new SingleInstanceMutex(uniqueName);
        Assert.True(second.IsFirstInstance);
    }
}
