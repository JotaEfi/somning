using Somning.Platform.Windows.Paste;
using Somning.Integration.Tests.Support;
using Xunit;

namespace Somning.Integration.Tests.Paste;

[Trait("Category", "Desktop")]
public sealed class IntegrityReaderTests
{
    [Fact]
    public void Reads_self_and_child_notepad_integrity()
    {
        Requires.Desktop();
        using var notepad = NotepadDriver.Start();
        var reader = new IntegrityReader();

        var own = reader.OwnRid();
        _ = Windows.Win32.PInvoke.GetWindowThreadProcessId(new Windows.Win32.Foundation.HWND(notepad.Window), out var processId);
        var child = reader.ReadRid(processId);

        Assert.Contains(own, new[] { 0x2000, 0x3000, 0x4000 });
        Assert.Equal(own, child);
    }

    [Fact]
    public void Handles_system_process_without_throwing()
    {
        Requires.Desktop();

        var result = new IntegrityReader().ReadRid(4);

        Assert.True(result is null or >= 0x4000);
    }
}
