using System.Windows.Threading;
using Somning.Core.Settings;
using Somning.Platform.Windows.Audio;

namespace Somning.Platform.Windows.Settings;

/// <summary>Hosts the WPF <see cref="SettingsView"/> on a dedicated STA dispatcher thread.</summary>
public sealed class SettingsWindowManager : ISettingsWindow
{
    private readonly Thread staThread;
    private readonly Dispatcher dispatcher;
    private readonly ISettingsProtector protector;
    private SettingsView? view;
    private volatile bool isOpen;
    private bool disposed;

    public SettingsWindowManager(ISettingsProtector? protector = null)
    {
        this.protector = protector ?? new DpapiSettingsProtector();
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        staThread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Somning.Settings.STA"
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        dispatcher = ready.Task.GetAwaiter().GetResult();
    }

    public bool IsOpen => isOpen;

    public void Show(UserSettings currentSettings, Action<UserSettings> onSave)
    {
        if (disposed) return;
        isOpen = true;
        dispatcher.BeginInvoke(() =>
        {
            if (view is not null)
            {
                view.Activate();
                return;
            }

            view = new SettingsView(currentSettings, AudioDeviceEnumerator.GetCaptureDevices(), protector);
            view.Saved += onSave;
            view.Closed += (_, _) =>
            {
                view = null;
                isOpen = false;
            };
            view.Show();
            view.Activate();
        });
    }

    public void Close()
    {
        if (disposed) return;
        isOpen = false;
        dispatcher.BeginInvoke(() => view?.Close());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dispatcher.BeginInvoke(() =>
        {
            view?.Close();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        });
        staThread.Join(1000);
    }
}
