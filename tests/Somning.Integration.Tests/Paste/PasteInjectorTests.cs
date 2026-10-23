using Somning.Platform.Windows.Input;
using Somning.Platform.Windows.Paste;
using Xunit;

namespace Somning.Integration.Tests.Paste;

public sealed class PasteInjectorTests
{
    [Theory]
    [InlineData(1, 0x11)]
    [InlineData(2, 0x56)]
    [InlineData(3, 0x11)]
    public void Partial_send_releases_only_keys_inserted_by_its_prefix(uint inserted, int expectedFirstCleanupKey)
    {
        var sender = new RecordingSender(inserted, cleanupInserted: inserted == 2 ? 2u : 1u);
        var result = new PasteInjector(sender).SendCtrlV();

        Assert.Equal(inserted, result.Inserted);
        Assert.True(result.CleanupSucceeded);
        Assert.Equal(2, sender.Batches.Count);
        var cleanup = sender.Batches[1];
        Assert.Equal(expectedFirstCleanupKey, cleanup[0].VirtualKey);
        Assert.True(cleanup[0].KeyUp);
        Assert.All(cleanup, input => Assert.Equal(InputMarkers.Paste, input.Marker));
        if (inserted == 2)
        {
            Assert.Equal(2, cleanup.Length);
            Assert.Equal((ushort)0x11, cleanup[1].VirtualKey);
        }
        else Assert.Single(cleanup);
    }

    [Fact]
    public void Partial_send_reports_cleanup_failure()
    {
        var sender = new RecordingSender(inserted: 2, cleanupInserted: 0);

        var result = new PasteInjector(sender).SendCtrlV();

        Assert.Equal(2u, result.Inserted);
        Assert.False(result.CleanupSucceeded);
    }

    [Fact]
    public void Partial_cleanup_retries_only_the_remaining_key_up()
    {
        var sender = new RecordingSender(inserted: 2, cleanupInserted: 1, retryInserted: 1);

        var result = new PasteInjector(sender).SendCtrlV();

        Assert.True(result.CleanupSucceeded);
        Assert.Equal(3, sender.Batches.Count);
        Assert.Single(sender.Batches[2]);
        Assert.Equal((ushort)0x11, sender.Batches[2][0].VirtualKey);
        Assert.True(sender.Batches[2][0].KeyUp);
    }

    private sealed class RecordingSender(uint inserted, uint cleanupInserted, uint retryInserted = 0) : IKeyboardInputSender
    {
        public List<KeyboardInput[]> Batches { get; } = [];
        public uint Send(IReadOnlyList<KeyboardInput> inputs)
        {
            Batches.Add(inputs.ToArray());
            return Batches.Count switch
            {
                1 => inserted,
                2 => cleanupInserted,
                _ => retryInserted
            };
        }
    }
}
