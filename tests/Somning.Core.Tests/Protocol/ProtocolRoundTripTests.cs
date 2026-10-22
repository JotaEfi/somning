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
    public void Rejects_malformed_message(string line)
    { Assert.False(JsonLines.TryDeserialize(line, out _, out var error)); Assert.Equal(ErrorCodes.BadRequest, error); }
    [Fact] public void Rejects_oversized_line() => Assert.False(JsonLines.TryDeserialize(new string('x', 70000), out _, out _));
}
