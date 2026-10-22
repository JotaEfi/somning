using Somning.Core.Engine;
namespace Somning.Core.Ports;
public interface ITranscriber : IAsyncDisposable
{
    Task StartAsync(string backend, CancellationToken ct);
    Task<EngineTranscription> TranscribeAsync(string wavPath, CancellationToken ct);
    EngineStatus Status { get; }
    event Action<EngineStatus>? StatusChanged;
    string? ActiveBackend { get; }
}
