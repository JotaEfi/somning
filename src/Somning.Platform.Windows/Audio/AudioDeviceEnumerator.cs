using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace Somning.Platform.Windows.Audio;

/// <summary>One active WASAPI capture endpoint, as shown in the settings microphone list (AUD-02).</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>
/// Lists active capture endpoints for the microphone picker. Enumeration is best effort: a machine without a
/// microphone, a missing Windows Audio service or a device that disappears mid-scan yields an empty/partial list
/// instead of an exception, so the settings dialog can always open.
/// </summary>
public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = TryGetDefaultId(enumerator);
            using var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            var devices = new List<AudioDeviceInfo>(endpoints.Count);
            for (var index = 0; index < endpoints.Count; index++)
            {
                using var device = endpoints[index];
                var id = device.ID;
                var isDefault = defaultId is not null &&
                    string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);
                devices.Add(new AudioDeviceInfo(id, ReadName(device, id), isDefault));
            }
            return devices;
        }
        catch (Exception exception) when (IsEnumerationFailure(exception))
        {
            return [];
        }
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator)
    {
        try
        {
            if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Console)) return null;

            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return device.ID;
        }
        catch (Exception exception) when (IsEnumerationFailure(exception))
        {
            return null;
        }
    }

    private static string ReadName(MMDevice device, string id)
    {
        try
        {
            var name = device.FriendlyName;
            return string.IsNullOrWhiteSpace(name) ? id : name;
        }
        catch (Exception exception) when (IsEnumerationFailure(exception))
        {
            return id;
        }
    }

    private static bool IsEnumerationFailure(Exception exception) =>
        exception is COMException or InvalidOperationException or PlatformNotSupportedException or UnauthorizedAccessException;
}
