using Somning.Core.Text;
using Xunit;
namespace Somning.Core.Tests.Text;
public sealed class WerTests
{
    [Theory]
    [InlineData("Olá, mundo!", "olá mundo", 0)]
    [InlineData("a b c d", "a x c d", .25)]
    [InlineData("", "", 0)]
    [InlineData("", "extra", 1)]
    public void Computes_word_distance(string expected, string actual, double distance) =>
        Assert.Equal(distance, Wer.Compute(expected, actual));
}
