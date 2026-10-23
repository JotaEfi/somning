namespace Somning.Platform.Windows.Processes;

public sealed class SingleInstanceMutex : IDisposable
{
    private const string DefaultMutexName = @"Local\Somning_SingleInstance_App_Mutex";
    private readonly Mutex? _mutex;
    private bool _disposed;

    public bool IsFirstInstance { get; }

    public SingleInstanceMutex(string mutexName = DefaultMutexName)
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
            IsFirstInstance = createdNew;
            if (!createdNew)
            {
                _mutex.Dispose();
                _mutex = null;
            }
        }
        catch (Exception)
        {
            IsFirstInstance = false;
            _mutex?.Dispose();
            _mutex = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            _mutex.Dispose();
        }
    }
}
