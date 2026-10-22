namespace Somning.Engine;
public sealed class GpuLogObserver
{
    private int gpu, error;
    public bool GpuInUse => Volatile.Read(ref gpu) != 0;
    public bool HasError => Volatile.Read(ref error) != 0;
    public void Observe(string? message)
    {
        if (message is null) return;
        if (message.Contains("using Vulkan0 backend", StringComparison.OrdinalIgnoreCase)) Interlocked.Exchange(ref gpu, 1);
        if (message.Contains("device lost", StringComparison.OrdinalIgnoreCase)
            || message.Contains("VK_ERROR", StringComparison.OrdinalIgnoreCase)) Interlocked.Exchange(ref error, 1);
    }
}
