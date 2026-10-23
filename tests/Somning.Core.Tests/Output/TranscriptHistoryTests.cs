using Somning.Core.Output;
using Xunit;

namespace Somning.Core.Tests.Output;

public sealed class TranscriptHistoryTests
{
    [Fact]
    public void Initially_has_no_transcript()
    {
        var history = new TranscriptHistory();

        Assert.Null(history.LastTranscript);
        Assert.False(history.HasTranscript);
    }

    [Fact]
    public void Record_stores_and_overwrites_with_the_new_valid_transcript()
    {
        var history = new TranscriptHistory();

        history.Record("primeiro texto");
        Assert.Equal("primeiro texto", history.LastTranscript);
        Assert.True(history.HasTranscript);

        history.Record("segundo texto");
        Assert.Equal("segundo texto", history.LastTranscript);
        Assert.True(history.HasTranscript);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n")]
    public void Blank_record_preserves_the_previous_valid_transcript(string blank)
    {
        var history = new TranscriptHistory();
        history.Record("transcrição válida");

        history.Record(blank);

        Assert.Equal("transcrição válida", history.LastTranscript);
        Assert.True(history.HasTranscript);
    }

    [Fact]
    public void Null_record_preserves_the_previous_valid_transcript()
    {
        var history = new TranscriptHistory();
        history.Record("transcrição válida");

        history.Record(null!);

        Assert.Equal("transcrição válida", history.LastTranscript);
        Assert.True(history.HasTranscript);
    }

    [Fact]
    public void Clear_resets_the_transcript()
    {
        var history = new TranscriptHistory();
        history.Record("texto");

        history.Clear();

        Assert.Null(history.LastTranscript);
        Assert.False(history.HasTranscript);
    }

    [Fact]
    public async Task Concurrent_records_always_leave_one_valid_transcript()
    {
        var history = new TranscriptHistory();
        var values = Enumerable.Range(0, 100).Select(index => $"texto-{index:D3}").ToArray();
        using var start = new ManualResetEventSlim(false);
        var cancellationToken = TestContext.Current.CancellationToken;

        var writers = values
            .Select(value => Task.Run(() =>
            {
                start.Wait(cancellationToken);
                history.Record(value);
            }, cancellationToken))
            .ToArray();

        start.Set();
        await Task.WhenAll(writers);

        var last = Assert.IsType<string>(history.LastTranscript);
        Assert.Contains(last, values);
        Assert.True(history.HasTranscript);
    }

    [Fact]
    public async Task Concurrent_record_and_clear_never_leaves_a_blank_transcript()
    {
        var history = new TranscriptHistory();
        using var start = new ManualResetEventSlim(false);
        var cancellationToken = TestContext.Current.CancellationToken;

        var writers = Enumerable.Range(0, 50)
            .Select(index => Task.Run(() =>
            {
                start.Wait(cancellationToken);
                history.Record($"texto-{index:D3}");
            }, cancellationToken))
            .ToArray();
        var clearer = Task.Run(() =>
        {
            start.Wait(cancellationToken);
            history.Clear();
        }, cancellationToken);

        start.Set();
        await Task.WhenAll(writers.Append(clearer));

        Assert.True(history.LastTranscript is null or { Length: > 0 });
        Assert.Equal(history.LastTranscript is not null, history.HasTranscript);
    }
}
