using System.Text;
using Somning.Engine.Protocol;
namespace Somning.Engine;
public sealed class ControlReader(EngineOptions options, InferenceExecutor executor, Action<EngineMessage> write)
{
    public void Run()
    {
        try
        {
            while (ReadBoundedLine(out var line))
            {
                if (line is null || !JsonLines.TryDeserialize(line, out var message, out _))
                { write(new ErrorMessage(null, ErrorCodes.BadRequest, "Invalid protocol line.")); continue; }
                switch (message)
                {
                    case TranscribeRequest t when string.IsNullOrWhiteSpace(t.Id) || t.Language != "pt":
                        write(new ErrorMessage(t.Id, ErrorCodes.BadRequest, "Invalid request.")); break;
                    case TranscribeRequest t when !PathGuard.Accepts(options.AllowedDir, t.WavPath):
                        write(new ErrorMessage(t.Id, ErrorCodes.InvalidPath, "Path is outside the audio boundary.")); break;
                    case TranscribeRequest t: executor.Enqueue(t); break;
                    case CancelRequest c when c.Id is not null: executor.Cancel(c.Id); break;
                    case ShutdownRequest: return;
                    default: write(new ErrorMessage(null, ErrorCodes.BadRequest, "Expected a request.")); break;
                }
            }
        }
        finally { executor.Stop(); }
    }
    private static bool ReadBoundedLine(out string? line)
    {
        var buffer = new StringBuilder(); bool overflow = false;
        while (true)
        {
            int c = Console.In.Read();
            if (c == -1 || c == '\n')
            {
                line = overflow ? null : buffer.ToString().TrimEnd('\r');
                return c != -1 || overflow || buffer.Length > 0;
            }
            if (buffer.Length < JsonLines.MaxLineLength) buffer.Append((char)c); else overflow = true;
        }
    }
}
