using Somning.Core.Cloud;
using Xunit;

namespace Somning.Core.Tests.Cloud;

public sealed class CloudProviderTests
{
    [Fact]
    public void Provider_values_match_the_settings_vocabulary()
    {
        Assert.Equal("none", CloudProvider.None);
        Assert.Equal("groq", CloudProvider.Groq);
        Assert.Equal("openai", CloudProvider.OpenAi);
    }

    [Fact]
    public void Groq_uses_whisper_large_v3_turbo()
    {
        Assert.Equal("whisper-large-v3-turbo", CloudProvider.DefaultModel(CloudProvider.Groq));
        Assert.Equal(
            "https://api.groq.com/openai/v1/audio/transcriptions",
            CloudProvider.DefaultEndpoint(CloudProvider.Groq));
    }

    [Fact]
    public void OpenAi_uses_whisper_1()
    {
        Assert.Equal("whisper-1", CloudProvider.DefaultModel(CloudProvider.OpenAi));
        Assert.Equal(
            "https://api.openai.com/v1/audio/transcriptions",
            CloudProvider.DefaultEndpoint(CloudProvider.OpenAi));
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("  GROQ ", "groq")]
    [InlineData("OpenAI", "openai")]
    public void Normalize_trims_and_lowercases_the_provider(string? input, string expected)
    {
        Assert.Equal(expected, CloudProvider.Normalize(input));
    }

    [Theory]
    [InlineData("groq", true)]
    [InlineData("OPENAI", true)]
    [InlineData("none", false)]
    [InlineData(null, false)]
    public void IsCloud_only_accepts_cloud_providers(string? provider, bool expected)
    {
        Assert.Equal(expected, CloudProvider.IsCloud(provider));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("unknown")]
    public void Local_or_unknown_providers_have_no_cloud_model_or_endpoint(string provider)
    {
        Assert.Throws<ArgumentException>(() => CloudProvider.DefaultModel(provider));
        Assert.Throws<ArgumentException>(() => CloudProvider.DefaultEndpoint(provider));
    }

    [Fact]
    public void Engine_config_normalizes_the_provider_and_validates_it()
    {
        var config = new CloudEngineConfig("  GROQ  ", "gsk_key_1234567890");

        Assert.Equal(CloudProvider.Groq, config.Provider);
        Assert.Equal("whisper-large-v3-turbo", config.ResolveModel());
        config.Validate();

        Assert.Throws<ArgumentException>(
            () => new CloudEngineConfig(CloudProvider.None, "gsk_key_1234567890").Validate());
        Assert.Throws<ArgumentException>(
            () => new CloudEngineConfig(CloudProvider.Groq, "   ").Validate());
    }

    [Fact]
    public void Engine_config_trims_the_vocabulary_and_ignores_blank_overrides()
    {
        var config = new CloudEngineConfig(CloudProvider.Groq, "gsk_key_1234567890", "  Somning, Groq  ", "   ");

        Assert.Equal("Somning, Groq", config.CustomVocabulary);
        Assert.Null(config.CustomEndpoint);
    }

    [Fact]
    public void Engine_config_uses_the_custom_endpoint_when_supplied()
    {
        var config = new CloudEngineConfig(
            CloudProvider.OpenAi,
            "sk-1234567890abcdef",
            customEndpoint: "http://localhost:54321/v1/audio/transcriptions");

        Assert.Equal(
            "http://localhost:54321/v1/audio/transcriptions",
            config.ResolveEndpoint().AbsoluteUri);
    }
}
