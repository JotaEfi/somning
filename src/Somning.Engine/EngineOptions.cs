using Somning.Core.Engine;
namespace Somning.Engine;
public sealed record EngineOptions(string Backend, string Model, string Vad, bool FlashAttention, int Threads, string AllowedDir)
{
    public static EngineOptions Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Arguments must be key/value pairs.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--backend" or "--model" or "--vad" or "--flash-attn" or "--threads" or "--allowed-dir")
                || !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown or duplicate argument.");
        }
        string Required(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v : throw new ArgumentException("Missing " + key);
        var backend = Required("--backend");
        if (backend is not ("cpu" or "vulkan")) throw new ArgumentException("Invalid backend.");
        var flash = values.GetValueOrDefault("--flash-attn", "on");
        if (flash is not ("on" or "off")) throw new ArgumentException("Invalid flash attention option.");
        if (!int.TryParse(values.GetValueOrDefault("--threads", EngineDefaults.Threads.ToString()), out int threads)
            || threads < 1 || threads > 128) throw new ArgumentException("Invalid threads.");
        return new(backend, Path.GetFullPath(Required("--model")), Path.GetFullPath(Required("--vad")),
            flash == "on", threads, Path.GetFullPath(Required("--allowed-dir")));
    }
}
