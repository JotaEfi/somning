using Somning.Core.Text;
using Xunit;
namespace Somning.Core.Tests.Text;
public sealed class TextNormalizerTests
{
    [Fact] public void Trims_paste() => Assert.Equal("Olá.", TextNormalizer.ForPaste("  Olá.\r\n "));
}
