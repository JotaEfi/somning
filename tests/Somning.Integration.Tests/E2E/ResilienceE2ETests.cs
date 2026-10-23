using System.Diagnostics;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.E2E;

[Trait("Category", "E2E")]
public sealed class ResilienceE2ETests
{
    [Fact]
    public async Task Killed_worker_during_request_restarts_on_cpu_and_pastes()
    {
        var fixture = E2EFixtureSupport.Fixture("tracer/hoje.wav");
        using var e2e = await E2EFixtureSupport.StartAsync(fixture,
            new Dictionary<string, string> { ["SOMNING_ENGINE_FAULT"] = "slow_transcribe" });
        try
        {
            var cursor = await e2e.App.LastSeqAsync();
            Assert.True(ForegroundHelper.EnsureForeground(e2e.Notepad.Window, e2e.Notepad.Edit));
            HotkeyInjector.Chord(70);
            await Task.Delay(E2EFixtureSupport.Duration(fixture) + TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);
            HotkeyInjector.Chord(70);
            var captured = await e2e.App.WaitEventAsync("target_captured", cursor, null, TimeSpan.FromSeconds(15));
            var started = await e2e.App.WaitEventAsync("transcribe_started", captured.Seq - 1, captured.Id, TimeSpan.FromSeconds(20));
            var pid = await WaitOwnedWorkerPid(e2e.App.Process.Id);
            using (var worker = Process.GetProcessById(pid)) worker.Kill(entireProcessTree: true);
            var outcome = await e2e.App.WaitEventAsync("outcome", started.Seq - 1, captured.Id, TimeSpan.FromMinutes(2));
            Assert.Equal("pasted", outcome.Outcome); Assert.Equal("cpu", outcome.Metrics?["backend"]?.GetValue<string>());
            e2e.AssertPasted(outcome, E2EFixtureSupport.Expected("tracer/hoje.txt"));
            Assert.False(e2e.App.Process.HasExited);
        }
        finally
        {
            var appPid = e2e.App.Process.Id;
            if (!e2e.App.Process.HasExited) await e2e.App.ShutdownAsync();
            await WaitForOwnedWorkersToExit(appPid);
            Assert.Empty(E2EFixtureSupport.EnginePids(appPid));
        }
    }

    [Fact]
    public async Task Vulkan_fault_retries_on_cpu_and_pastes()
    {
        var fixture = E2EFixtureSupport.Fixture("tracer/hoje.wav");
        using var e2e = await E2EFixtureSupport.StartAsync(fixture,
            new Dictionary<string, string> { ["SOMNING_ENGINE_FAULT"] = "gpu_error_on_transcribe" });
        var outcome = await e2e.DictateAsync(fixture, hold: false);
        Assert.Equal("pasted", outcome.Outcome); Assert.Equal("cpu", outcome.Metrics?["backend"]?.GetValue<string>());
        e2e.AssertPasted(outcome, E2EFixtureSupport.Expected("tracer/hoje.txt"));
        Assert.False(e2e.App.Process.HasExited);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task App_crash_recording_is_recovered_on_restart(bool completeCapture)
    {
        var fixture = E2EFixtureSupport.Fixture("tracer/hoje.wav");
        var dataDir = Path.Combine(Path.GetTempPath(), "somning-recovery-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        using var notepad = NotepadDriver.Start();
        try
        {
            E2EFixtureSupport.SetClipboard("RECOVERY-SENTINEL");
            Assert.True(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit));
            var app = await AppHarnessDriver.StartAsync(fixture, waitForEngine: true, dataDir: dataDir);
            try
            {
                await app.WaitEventAsync("recovery_complete", 0, null, TimeSpan.FromSeconds(30));
                var cursor = await app.LastSeqAsync();
                Assert.True(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit));
                if (completeCapture) await app.ArmDeliveryBarrierAsync();
                HotkeyInjector.Chord(70);
                if (completeCapture)
                {
                    await Task.Delay(E2EFixtureSupport.Duration(fixture) + TimeSpan.FromMilliseconds(800), TestContext.Current.CancellationToken);
                    Assert.True(ForegroundHelper.EnsureForeground(notepad.Window, notepad.Edit)); HotkeyInjector.Chord(70);
                    var captured = await app.WaitEventAsync("target_captured", cursor, null, TimeSpan.FromSeconds(20));
                    await app.WaitEventAsync("transcribe_started", captured.Seq - 1, captured.Id, TimeSpan.FromSeconds(20));
                    await WaitForSavedText(Path.Combine(app.RecordingsDir, captured.Id + ".json"), TimeSpan.FromSeconds(30));
                }
                else await WaitForPcm(app.RecordingsDir, TimeSpan.FromSeconds(10), 6.5);
                app.Process.Kill(entireProcessTree: true); await app.Process.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            finally { app.Dispose(); }
            var wav = Assert.Single(Directory.GetFiles(Path.Combine(dataDir, "recordings"), "*.wav"));
            Assert.True(new FileInfo(wav).Length > 44);
            using var restarted = await AppHarnessDriver.StartAsync(fixture, waitForEngine: true, dataDir: dataDir);
            var recovered = await restarted.WaitEventAsync("recovery_complete", 0, null, TimeSpan.FromMinutes(2));
            _ = recovered;
            var outcome = await restarted.WaitEventAsync("outcome", 0, null, TimeSpan.FromMinutes(2));
            Assert.Equal("recovered", outcome.Outcome);
            Assert.NotEmpty(ClipboardProbe.ReadText()); Assert.NotEqual("RECOVERY-SENTINEL", ClipboardProbe.ReadText());
            Assert.Equal(string.Empty, notepad.GetText());
            Assert.True(File.Exists(Path.ChangeExtension(wav, ".json")));
        }
        finally { HotkeyInjector.ReleaseAll(); notepad.Dispose(); if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); }
    }

    private static async Task<int> WaitOwnedWorkerPid(int appPid)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var pids = E2EFixtureSupport.EnginePids(appPid);
            if (pids.Length == 1) return int.Parse(pids[0]);
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("owned engine worker process was not found");
    }

    private static async Task WaitForPcm(string recordingsDir, TimeSpan timeout, double secondsOfPcm)
    {
        var until = DateTime.UtcNow + timeout;
        var requiredBytes = 44 + (long)(secondsOfPcm * 16000 * 2);
        while (DateTime.UtcNow < until)
        {
            var wav = Directory.GetFiles(recordingsDir, "*.wav").FirstOrDefault();
            if (wav is not null && new FileInfo(wav).Length >= requiredBytes) return;
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException($"durable capture did not reach {secondsOfPcm:F1} s of PCM");
    }

    private static async Task WaitForOwnedWorkersToExit(int appPid)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < until && E2EFixtureSupport.EnginePids(appPid).Length > 0)
            await Task.Delay(50, TestContext.Current.CancellationToken);
    }

    private static async Task WaitForSavedText(string sidecarPath, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            if (File.Exists(sidecarPath))
            {
                try
                {
                    using var sidecar = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(sidecarPath));
                    if (!string.IsNullOrWhiteSpace(sidecar.RootElement.GetProperty("text").GetString())) return;
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException) { }
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("completed capture text was not durably saved before the App crash");
    }
}
