using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;
using Somning.Core.Paths;
using Somning.Core.Update;
using Somning.Platform.Windows.Update;

namespace Somning.App.Update;

/// <summary>
/// Looks for a new GitHub release shortly after start and then once a day. A new version is never installed without a
/// yes from the user, the installer is verified against the SHA-256 published by GitHub, and nothing is shown while a
/// dictation is in progress.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string ProbeModelFile = "ggml-large-v3-turbo-q5_0.bin";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(5);

    private readonly Func<bool> isEnabled;
    private readonly Func<bool> isBusy;
    private readonly Action requestShutdown;
    private readonly UpdateChecker checker;
    private readonly UpdateDownloader downloader;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim running = new(1, 1);
    private Version? promptedVersion;

    public UpdateService(Func<bool> isEnabled, Func<bool> isBusy, Action requestShutdown, UpdateChecker? checker = null, UpdateDownloader? downloader = null)
    {
        this.isEnabled = isEnabled;
        this.isBusy = isBusy;
        this.requestShutdown = requestShutdown;
        this.checker = checker ?? new UpdateChecker();
        this.downloader = downloader ?? new UpdateDownloader();
    }

    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(0, 0, 0);

    public void StartBackground()
    {
        _ = Task.Run(() => LoopAsync(stop.Token));
        _ = Task.Run(DeleteOldInstallers);
    }

    /// <summary>Removes installers left by a previous update. The one that is still running (after a relaunch) is skipped.</summary>
    private static void DeleteOldInstallers()
    {
        try
        {
            var directory = Path.Combine(SomningPaths.DataRoot, "updates");
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.GetFiles(directory))
            {
                try { File.Delete(file); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Debug(ex, "Could not clean the updates folder");
        }
    }

    /// <summary>Tray command: checks right away and always tells the person the result.</summary>
    public Task CheckNowAsync() => Task.Run(() => RunAsync(manual: true, stop.Token));

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(StartupDelay, token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                if (isEnabled())
                {
                    var delivered = await RunAsync(manual: false, token).ConfigureAwait(false);
                    if (!delivered) { await Task.Delay(BusyRetry, token).ConfigureAwait(false); continue; }
                }
                await Task.Delay(CheckInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <returns>False when the check should be retried soon (a dictation was in progress).</returns>
    private async Task<bool> RunAsync(bool manual, CancellationToken token)
    {
        if (!await running.WaitAsync(0, token).ConfigureAwait(false)) return true;
        try
        {
            var current = CurrentVersion;
            var update = await checker.CheckAsync(current, RuntimeInformation.ProcessArchitecture == Architecture.Arm64, token).ConfigureAwait(false);
            if (update is null)
            {
                if (manual) UpdateDialogs.ShowUpToDate(current);
                return true;
            }
            if (!manual && update.Version == promptedVersion) return true;
            if (!manual && isBusy()) return false;
            promptedVersion = update.Version;
            Log.Information("Update available: {Current} -> {Latest}", current, update.Version);
            if (!UpdateDialogs.AskToUpdate(current, update.Version)) return true;

            var directory = Path.Combine(SomningPaths.DataRoot, "updates");
            var installer = await downloader.DownloadAsync(update, directory, cancellationToken: token).ConfigureAwait(false);
            Log.Information("Update {Version} verified; starting installer", update.Version);
            UpdateDialogs.LaunchInstaller(installer, File.Exists(Path.Combine(SomningPaths.ModelsDir, ProbeModelFile)));
            requestShutdown();
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return true; }
        catch (Exception ex) when (ex is HttpRequestException or UpdateVerificationException or IOException or InvalidOperationException or TaskCanceledException)
        {
            Log.Warning(ex, "Update check or install failed");
            if (manual || promptedVersion is not null) UpdateDialogs.ShowFailure(Describe(ex));
            return true;
        }
        finally { running.Release(); }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException or TaskCanceledException => "Verifique a conexão com a internet.",
        UpdateVerificationException => "O arquivo baixado não passou na verificação de segurança e foi descartado.",
        _ => ex.Message,
    };

    public void Dispose()
    {
        try { stop.Cancel(); } catch (ObjectDisposedException) { }
        stop.Dispose();
    }
}
