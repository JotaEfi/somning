#if SOMNING_TEST_HARNESS
using Somning.Engine.Protocol;
namespace Somning.Engine;
internal static class FaultInjection
{
    public static async Task<string?> BeforeTranscribeAsync(string backend, CancellationToken token)
    {
        switch (Environment.GetEnvironmentVariable("SOMNING_ENGINE_FAULT"))
        {
            // Kill, not FailFast: FailFast goes through Windows Error Reporting, which pops an "Unknown Hard Error"
            // dialog on the desktop during test runs. An abrupt kill is the same crash for the supervisor.
            case "crash_on_transcribe":
                Console.Error.WriteLine("somning-test:injected-crash");
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                break;
            case "gpu_error_on_transcribe" when backend == "vulkan": return ErrorCodes.GpuError;
            case "slow_transcribe":
                Console.Error.WriteLine("somning-test:inference-started");
                await Task.Delay(TimeSpan.FromSeconds(60), token); break;
        }
        return null;
    }
    public static Task DelayReadyAsync() => int.TryParse(Environment.GetEnvironmentVariable("SOMNING_ENGINE_READY_DELAY_MS"),
        out int delay) && delay > 0 ? Task.Delay(delay) : Task.CompletedTask;
}
#endif
