namespace Somning.Integration.Tests.Support;
public sealed class HotkeyTestWindow : IDisposable
{
    public void Dispose()
    {
        HotkeyInjector.ReleaseAll();
    }
}
