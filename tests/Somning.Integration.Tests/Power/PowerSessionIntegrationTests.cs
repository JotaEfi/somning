using System.Runtime.InteropServices;
using System.Threading.Channels;
using Somning.App.Composition;
using Somning.Core.Settings;
using Somning.Core.Trigger;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Hook;
using Somning.Platform.Windows.Power;
using Xunit;

namespace Somning.Integration.Tests.Power;

/// <summary>
/// DIST-03: the power/session message window must raise resume/lock/unlock events and the hook must be
/// reinstalled after a sleep/resume or lock/unlock cycle without losing keyboard events.
/// </summary>
[Trait("Category", "Desktop")]
public sealed class PowerSessionIntegrationTests
{
    private const uint WmPowerBroadcast = 0x0218;
    private const uint WmWtsSessionChange = 0x02B1;
    private const uint PbtApmSuspend = 0x0004;
    private const uint PbtApmResumeAutomatic = 0x0012;
    private const uint PbtApmResumeSuspend = 0x0007;
    private const nuint WtsSessionLock = 0x7;
    private const nuint WtsSessionUnlock = 0x8;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public UserSettings Current { get; set; } = new();
        public string FilePath => "memory://power-settings.json";

        public Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    private static Channel<KeyEvent> Queue() => Channel.CreateBounded<KeyEvent>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private static async Task<KeyEvent> Read(Channel<KeyEvent> queue) =>
        await queue.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

    [Fact]
    public void PowerAndSessionMessages_RaiseTheMatchingEvents()
    {
        Requires.Desktop();
        using var listener = new PowerSessionListener();
        int suspended = 0, resumed = 0, locked = 0, unlocked = 0;
        listener.Suspended += () => Interlocked.Increment(ref suspended);
        listener.Resumed += () => Interlocked.Increment(ref resumed);
        listener.SessionLocked += () => Interlocked.Increment(ref locked);
        listener.SessionUnlocked += () => Interlocked.Increment(ref unlocked);

        Assert.True(PostMessage(listener.Handle, WmPowerBroadcast, PbtApmSuspend, 0));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref suspended) == 1, Timeout));

        Assert.True(PostMessage(listener.Handle, WmPowerBroadcast, PbtApmResumeAutomatic, 0));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref resumed) == 1, Timeout));

        // PBT_APMRESUMESUSPEND must raise the same resumed signal.
        Assert.True(PostMessage(listener.Handle, WmPowerBroadcast, PbtApmResumeSuspend, 0));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref resumed) == 2, Timeout));

        Assert.True(PostMessage(listener.Handle, WmWtsSessionChange, WtsSessionLock, 0));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref locked) == 1, Timeout));

        Assert.True(PostMessage(listener.Handle, WmWtsSessionChange, WtsSessionUnlock, 0));
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref unlocked) == 1, Timeout));
    }

    [Fact]
    public async Task HookWatchdog_ReinstallRefreshesHookAndKeepsChannelOperational()
    {
        Requires.Desktop();
        using var window = new HotkeyTestWindow();
        var queue = Queue();
        using var hook = new KeyboardHook(queue.Writer);
        hook.Start();
        try
        {
            int installs = hook.InstallCount;
            Assert.True(installs >= 1);
            Assert.True(hook.IsHookActive);
            Assert.NotEqual(0, hook.HookHandle);

            hook.Reinstall();

            Assert.True(SpinWait.SpinUntil(() => hook.InstallCount > installs, Timeout));
            Assert.True(hook.IsHookActive);
            Assert.NotEqual(0, hook.HookHandle);

            // The reinstalled hook still feeds the keyboard event channel.
            HotkeyInjector.Chord(50);
            Assert.Equal(KeyEventKind.TriggerDown, (await Read(queue)).Kind);
            Assert.Equal(KeyEventKind.TriggerUp, (await Read(queue)).Kind);
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }

    [Fact]
    public async Task ResumeAndUnlock_ReinstallTheAppKeyboardHook()
    {
        Requires.Desktop();
        using var window = new HotkeyTestWindow();
        var listener = new PowerSessionListener();
        await using var app = new AppComposition(
            () => new FakeAudioSource(),
            settingsStore: new MemorySettingsStore(),
            powerListener: listener);
        Assert.Same(listener, app.PowerListener);

        app.Hook.Start();
        try
        {
            int installs = app.Hook.InstallCount;
            Assert.True(installs >= 1);
            Assert.True(app.Hook.IsHookActive);

            // Waking from sleep must reinstall the hook through the PowerSessionListener wiring.
            Assert.True(PostMessage(listener.Handle, WmPowerBroadcast, PbtApmResumeAutomatic, 0));
            Assert.True(SpinWait.SpinUntil(() => app.Hook.InstallCount > installs, Timeout));
            Assert.True(app.Hook.IsHookActive);

            int afterResume = app.Hook.InstallCount;

            // Unlocking the workstation must reinstall it again.
            Assert.True(PostMessage(listener.Handle, WmWtsSessionChange, WtsSessionUnlock, 0));
            Assert.True(SpinWait.SpinUntil(() => app.Hook.InstallCount > afterResume, Timeout));
            Assert.True(app.Hook.IsHookActive);
        }
        finally { HotkeyInjector.ReleaseAll(); }
    }
}
