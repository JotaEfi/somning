using Somning.Core.Ports;

namespace Somning.Platform.Windows.Audio;

/// <summary>
/// Creates a fresh <see cref="IAudioSource"/> for every capture, using whatever microphone is selected at that
/// moment. Changing <see cref="CurrentDeviceId"/> therefore applies to the next recording without an app restart
/// (AUD-02). The field is read with <see cref="Volatile.Read{T}(ref T)"/> so the UI thread and the capture loop can
/// safely race.
/// </summary>
public sealed class DynamicAudioSourceFactory
{
    private string? currentDeviceId;

    public DynamicAudioSourceFactory(string? initialDeviceId = null) => CurrentDeviceId = initialDeviceId;

    /// <summary>WASAPI endpoint id for the next capture, or <c>null</c> for the Windows default device.</summary>
    public string? CurrentDeviceId
    {
        get => Volatile.Read(ref currentDeviceId);
        set => Volatile.Write(ref currentDeviceId, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
    }

    /// <summary>Changes the microphone used by subsequent captures (alias kept for the settings command handlers).</summary>
    public void SetDevice(string? deviceId) => CurrentDeviceId = deviceId;

    public IAudioSource CreateAudioSource() => new WasapiAudioSource(CurrentDeviceId);
}
