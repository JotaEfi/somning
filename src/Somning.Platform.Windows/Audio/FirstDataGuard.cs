namespace Somning.Platform.Windows.Audio;

public sealed class AudioDeviceUnavailableException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public static class FirstDataGuard
{
    public static async Task WaitAsync(Task firstData, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(firstData);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        try
        {
            await firstData.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new AudioDeviceUnavailableException("no_audio_data", "No audio buffer arrived before the capture deadline.", exception);
        }
    }
}
