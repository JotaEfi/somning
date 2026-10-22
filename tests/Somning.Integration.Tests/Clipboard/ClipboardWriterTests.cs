using System.Diagnostics;
using Somning.Integration.Tests.Support;
using Somning.Platform.Windows.Clipboard;
using Xunit;
namespace Somning.Integration.Tests.Clipboard;
public sealed class ClipboardWriterTests
{
    [Fact]
    public void Sets_unicode_text_and_changes_sequence()
    {
        Requires.Desktop();
        using var writer = new ClipboardWriter();
        var result = writer.SetText("Olá, ação.");
        Assert.True(result.Success, result.Error);
        Assert.NotEqual(result.SequenceBefore, result.SequenceAfter);
        Assert.Equal("Olá, ação.", ClipboardProbe.ReadText());
    }
    [Fact]
    public void Held_clipboard_returns_failure_after_ten_attempts()
    {
        Requires.Desktop();
        using var holder = new ClipboardHolder();
        using var writer = new ClipboardWriter();
        var watch = Stopwatch.StartNew();
        var result = writer.SetText("This write must fail.");
        Assert.False(result.Success); Assert.Equal(10, result.Attempts); Assert.NotNull(result.Error);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3));
    }
    [Fact]
    public async Task Concurrent_window_disposal_completes_invocations()
    {
        Requires.Desktop();
        for (int iteration = 0; iteration < 30; iteration++)
        {
            using var owner = new MessageOnlyWindow();
            using var start = new ManualResetEventSlim();
            var calls = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                start.Wait();
                try { return owner.Invoke(() => 1); }
                catch (ObjectDisposedException) { return 0; }
            })).ToArray();
            var close = Task.Run(() => { start.Wait(); owner.Dispose(); }, TestContext.Current.CancellationToken);
            start.Set();
            await Task.WhenAll(calls.Cast<Task>().Append(close)).WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        }
    }
    [Fact]
    public async Task Timed_out_pending_work_is_not_executed()
    {
        Requires.Desktop();
        using var owner = new MessageOnlyWindow();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var token = TestContext.Current.CancellationToken;
        bool mutated = false;
        var busy = Task.Run(() =>
        {
            try { owner.Invoke(() => { entered.Set(); release.Wait(token); return 0; }); }
            catch (TimeoutException) { }
        }, token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2), token));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Task.Run(() =>
                owner.Invoke(() => { mutated = true; return 0; }), token));
        }
        finally { release.Set(); }
        await busy.WaitAsync(TimeSpan.FromSeconds(2), token);
        owner.Invoke(() => 0); // The dispatcher has now passed the cancelled operation.
        Assert.False(mutated);
    }
}
