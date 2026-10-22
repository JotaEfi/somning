namespace Somning.Core.Engine;
public sealed record EngineSessionInfo(string Backend, string Model, long LoadMs, long WarmMs,
    bool GpuInUse, bool FlashAttention);
