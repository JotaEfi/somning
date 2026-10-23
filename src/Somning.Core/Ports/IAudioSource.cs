namespace Somning.Core.Ports;

public interface IAudioSource : IAsyncDisposable
{
    Task<AudioStartInfo> StartAsync(IPcmSink sink, CancellationToken ct);
    Task StopAsync();

    event Action<float>? Level;
    event Action<AudioFault>? Faulted;
}

public sealed record AudioStartInfo(long MicOpenMs, string DeviceFormat);

public sealed record AudioFault(string Code, string Message);
