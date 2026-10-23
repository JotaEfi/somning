using Somning.Platform.Windows.Audio;
using Xunit;

namespace Somning.Integration.Tests.Audio;

public sealed class DynamicAudioSourceFactoryTests
{
    [Fact]
    public async Task Factory_uses_the_default_device_when_nothing_is_selected()
    {
        var factory = new DynamicAudioSourceFactory();

        await using var source = Assert.IsType<WasapiAudioSource>(factory.CreateAudioSource());

        Assert.Null(factory.CurrentDeviceId);
        Assert.Null(source.DeviceId);
    }

    [Fact]
    public async Task Changing_the_device_applies_to_the_next_source_without_touching_the_current_one()
    {
        var factory = new DynamicAudioSourceFactory("device-a");

        await using var first = Assert.IsType<WasapiAudioSource>(factory.CreateAudioSource());

        factory.CurrentDeviceId = "device-b";
        await using var second = Assert.IsType<WasapiAudioSource>(factory.CreateAudioSource());

        Assert.Equal("device-a", first.DeviceId);
        Assert.Equal("device-b", second.DeviceId);
        Assert.Equal("device-b", factory.CurrentDeviceId);
    }

    [Fact]
    public async Task Set_device_trims_values_and_blank_means_default()
    {
        var factory = new DynamicAudioSourceFactory();

        factory.SetDevice("  device-c  ");
        await using var selected = Assert.IsType<WasapiAudioSource>(factory.CreateAudioSource());
        Assert.Equal("device-c", factory.CurrentDeviceId);
        Assert.Equal("device-c", selected.DeviceId);

        factory.SetDevice("   ");
        await using var fallback = Assert.IsType<WasapiAudioSource>(factory.CreateAudioSource());
        Assert.Null(factory.CurrentDeviceId);
        Assert.Null(fallback.DeviceId);

        factory.SetDevice("device-d");
        factory.SetDevice(null);
        Assert.Null(factory.CurrentDeviceId);
    }

    [Fact]
    public void Initial_device_id_is_normalized()
    {
        Assert.Null(new DynamicAudioSourceFactory("").CurrentDeviceId);
        Assert.Null(new DynamicAudioSourceFactory("  ").CurrentDeviceId);
        Assert.Null(new DynamicAudioSourceFactory(null).CurrentDeviceId);
        Assert.Equal("device-e", new DynamicAudioSourceFactory(" device-e ").CurrentDeviceId);
    }
}
