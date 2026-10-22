using System.Globalization;
using Somning.Engine.Protocol;
using Xunit;
namespace Somning.Core.Tests.Protocol;
public sealed class ProtocolRoundTripTests
{
    [Fact]
    public void Accents_and_numbers_round_trip()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            var message = new ResultMessage("x", "Olá, ação, João, até às 3h", "text", null, 1, 0, 1234.5, 0, "cpu", "model");
            var line = JsonLines.Serialize(message);
            Assert.Contains("1234.5", line);
            Assert.True(JsonLines.TryDeserialize(line, out var roundtrip, out _));
            Assert.Equal(message, Assert.IsType<ResultMessage>(roundtrip));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Theory]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("not json")]
    [InlineData(null)]
    public void Rejects_malformed_message(string? line)
    { Assert.False(JsonLines.TryDeserialize(line, out _, out var error)); Assert.Equal(ErrorCodes.BadRequest, error); }
    [Fact] public void Rejects_oversized_line() => Assert.False(JsonLines.TryDeserialize(new string('x', 70000), out _, out _));
    [Fact]
    public void Segments_round_trip_with_timing_and_accents()
    {
        var segment = new SegmentMessage("Olá, ação.", 120.5, 1400.25, .1f);
        var message = new ResultMessage("segments", segment.Text, "text", null, 1500, 0, 12, .1f,
            "cpu", "model", [segment]);
        Assert.True(JsonLines.TryDeserialize(JsonLines.Serialize(message), out var roundtrip, out _));
        Assert.Equal(segment, Assert.Single(Assert.IsType<ResultMessage>(roundtrip).Segments!));
    }
    [Fact]
    public void Type_discriminator_can_follow_properties()
    {
        Assert.True(JsonLines.TryDeserialize(
            "{\"id\":\"ordered\",\"wav_path\":\"a.wav\",\"language\":\"pt\",\"type\":\"transcribe\"}", out var message, out _));
        Assert.Equal("ordered", Assert.IsType<TranscribeRequest>(message).Id);
    }
}
