using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class AudioDeviceEnumeratorTests
{
    [Fact]
    public void Capturing_devices_never_throws_and_returns_a_consistent_snapshot()
    {
        var devices = AudioDeviceEnumerator.GetCaptureDevices();

        Assert.NotNull(devices);
        Assert.All(devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
        });

        // A machine may have no microphone (empty list) or many, but never two "default" entries.
        Assert.InRange(devices.Count(device => device.IsDefault), 0, 1);
        Assert.Equal(devices.Count, devices.Select(device => device.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Enumerating_twice_does_not_throw_and_keeps_the_default_endpoint_stable()
    {
        var first = AudioDeviceEnumerator.GetCaptureDevices();
        var second = AudioDeviceEnumerator.GetCaptureDevices();

        Assert.NotNull(first);
        Assert.NotNull(second);

        var firstDefault = first.SingleOrDefault(device => device.IsDefault);
        var secondDefault = second.SingleOrDefault(device => device.IsDefault);
        Assert.Equal(firstDefault?.Id, secondDefault?.Id);
    }
}
