using Somning.Core.Engine;

namespace Somning.App.Engine;

public sealed record SupervisorOptions(
    TimeSpan? ReadinessTimeout = null,
    int RequestTimeoutFactor = 3,
    TimeSpan? MinRequestTimeout = null,
    TimeSpan? ShutdownTimeout = null,
    IClock? Clock = null,
    IReadOnlyDictionary<string, string>? FirstWorkerEnvironment = null,
    string? ModelsDirOverride = null,
    bool ZeroBackoff = false)
{
    public TimeSpan EffectiveReadinessTimeout => ReadinessTimeout ?? TimeSpan.FromSeconds(60);
    public TimeSpan EffectiveMinRequestTimeout => MinRequestTimeout ?? TimeSpan.FromSeconds(30);
    public TimeSpan EffectiveShutdownTimeout => ShutdownTimeout ?? TimeSpan.FromSeconds(5);
}
