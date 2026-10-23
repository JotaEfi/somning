using Somning.Core.Text;
using Xunit;

namespace Somning.Core.Tests.Text;

public sealed class PostFilterTests
{
    [Theory]
    [InlineData("Obrigado.")]
    [InlineData("obrigado por assistir!")]
    [InlineData("Legendas pela comunidade Amara.org")]
    [InlineData("amara.org")]
    [InlineData("Inscreva-se")]
    [InlineData("INSCREVA SE!")]
    [InlineData("Inscreva-se no canal.")]
    [InlineData("Aplausos.")]
    [InlineData("[Música]")]
    [InlineData("Legenda por Sônia Ruberti")]
    public void Drops_whole_output_hallucinations(string text)
    {
        var result = PostFilter.Evaluate(text, 0, .6f);
        Assert.False(result.Keep);
        Assert.Equal("blocklist", result.Reason);
    }

    [Theory]
    [InlineData("Obrigado pela ajuda com o relatório.")]
    [InlineData("Inscreva-se no meu curso de Python amanhã.")]
    [InlineData("E aí")]
    [InlineData("E aí, tudo bem?")]
    [InlineData("Sim.")]
    [InlineData("Ok.")]
    [InlineData("Não.")]
    [InlineData("12")]
    public void Keeps_valid_text_including_short_utterances(string text)
    {
        var result = PostFilter.Evaluate(text, 0, .6f);
        Assert.True(result.Keep);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData(" - ")]
    [InlineData("")]
    public void Drops_outputs_without_letters_or_digits(string text)
    {
        var result = PostFilter.Evaluate(text, 0, .6f);
        Assert.False(result.Keep);
        Assert.Equal("no_letters", result.Reason);
    }

    [Fact]
    public void Applies_no_speech_probability_after_text_filters()
    {
        Assert.Equal("no_speech_prob", PostFilter.Evaluate("palavra", .7f, .6f).Reason);
        Assert.True(PostFilter.Evaluate("palavra", 0, .6f).Keep);
    }

    [Fact]
    public void Normalizes_entries_and_outputs_with_one_representation()
    {
        Assert.Equal(PostFilter.Normalize("Inscreva-se!"), PostFilter.Normalize("inscreva se"));
        Assert.Equal("amara org", PostFilter.Normalize("Amara.org"));
        Assert.Equal("música", PostFilter.Normalize("[Música]"));
    }
}
