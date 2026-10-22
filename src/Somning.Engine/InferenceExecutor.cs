using System.Diagnostics;
using System.Threading.Channels;
using Somning.Engine.Protocol;
namespace Somning.Engine;
public sealed class InferenceExecutor(WhisperHost host, EngineOptions options, GpuLogObserver observer,
    Action<EngineMessage> write) : IDisposable
{
    private sealed record Pending(TranscribeRequest Request, CancellationTokenSource Cancellation);
    private readonly Channel<Pending> queue = Channel.CreateBounded<Pending>(32);
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);
    private readonly object sync = new();
    private bool stopping;
    public void Enqueue(TranscribeRequest request)
    {
        lock (sync)
        {
            if (stopping) return;
            if (pending.ContainsKey(request.Id)) { write(new ErrorMessage(request.Id, ErrorCodes.BadRequest, "Duplicate id.")); return; }
            var item = new Pending(request, new CancellationTokenSource());
            if (!queue.Writer.TryWrite(item))
            { item.Cancellation.Dispose(); write(new ErrorMessage(request.Id, ErrorCodes.BadRequest, "Queue full.")); return; }
            pending.Add(request.Id, item);
        }
    }
    public void Cancel(string id)
    {
        lock (sync) { if (pending.TryGetValue(id, out var item)) item.Cancellation.Cancel(); }
    }
    public void Stop()
    {
        lock (sync)
        {
            stopping = true;
            foreach (var item in pending.Values) item.Cancellation.Cancel();
            queue.Writer.TryComplete();
        }
    }
    public async Task RunAsync()
    {
        await foreach (var item in queue.Reader.ReadAllAsync())
        {
            await ExecuteAsync(item);
            lock (sync) { pending.Remove(item.Request.Id); item.Cancellation.Dispose(); }
        }
    }
    private async Task ExecuteAsync(Pending item)
    {
        var request = item.Request; var token = item.Cancellation.Token;
        try
        {
            token.ThrowIfCancellationRequested();
#if SOMNING_TEST_HARNESS
            var fault = await FaultInjection.BeforeTranscribeAsync(options.Backend, token);
            if (fault is not null) { write(new ErrorMessage(request.Id, fault, "Injected GPU error.")); return; }
#endif
            var watch = Stopwatch.StartNew();
            var (text, noSpeech) = await host.TranscribeAsync(request.WavPath, token);
            write(new ResultMessage(request.Id, text, text.Length == 0 ? "empty" : "text",
                text.Length == 0 ? "blank" : null, WavDurationMs(request.WavPath), 0,
                watch.Elapsed.TotalMilliseconds, noSpeech, options.Backend, Path.GetFileName(options.Model)));
        }
        catch (OperationCanceledException) { write(new ErrorMessage(request.Id, ErrorCodes.Cancelled, "Request cancelled.")); }
        catch (Exception) { write(new ErrorMessage(request.Id, observer.HasError ? ErrorCodes.GpuError
            : ErrorCodes.TranscribeFailed, "Transcription failed.")); }
    }
    private static double WavDurationMs(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not RIFF.");
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not WAVE.");
        uint rate = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = new string(reader.ReadChars(4)); var size = reader.ReadUInt32();
            var end = reader.BaseStream.Position + size + (size % 2);
            if (end > reader.BaseStream.Length + 1) throw new InvalidDataException("Truncated WAV.");
            if (id == "fmt " && size >= 16)
            { reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32(); rate = reader.ReadUInt32(); }
            if (id == "data") return rate > 0 ? size * 1000.0 / rate : throw new InvalidDataException("Missing format.");
            reader.BaseStream.Position = end;
        }
        throw new InvalidDataException("Missing audio.");
    }
    public void Dispose() { Stop(); }
}
