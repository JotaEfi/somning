using Somning.Core.Paths;
using Xunit;
namespace Somning.Core.Tests.Paths;
public sealed class PathContainmentTests
{
    [Theory]
    [InlineData(@"C:\data\rec\a.wav", true)]
    [InlineData(@"c:\DATA\REC\sub\b.wav", true)]
    [InlineData(@"C:\data\rec-other\x.wav", false)]
    [InlineData(@"C:\data\rec\..\x.wav", false)]
    [InlineData(@"C:\data\rec", false)]
    [InlineData(@"D:\x.wav", false)]
    public void Checks_separator_boundary(string candidate, bool expected) =>
        Assert.Equal(expected, PathContainment.IsStrictlyUnder(@"C:\data\rec", candidate));
}
