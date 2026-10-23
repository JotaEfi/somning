using Somning.Core.Cloud;
using Xunit;

namespace Somning.Core.Tests.Cloud;

public sealed class ApiKeyMaskerTests
{
    private const string LongKey = "gsk_1234567890abcdefghijklmnop";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_keys_mask_to_an_empty_string(string? key)
    {
        Assert.Equal(string.Empty, ApiKeyMasker.Mask(key));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1234567")]
    [InlineData("12345678")]
    public void Keys_shorter_than_nine_characters_are_fully_masked(string key)
    {
        Assert.Equal(ApiKeyMasker.MaskToken, ApiKeyMasker.Mask(key));
    }

    [Fact]
    public void Long_keys_keep_only_the_first_and_last_three_characters()
    {
        Assert.Equal("gsk***nop", ApiKeyMasker.Mask(LongKey));
        Assert.Equal("abc***789", ApiKeyMasker.Mask("abc456789"));
    }

    [Fact]
    public void Masking_never_exposes_the_secret_middle_of_the_key()
    {
        var masked = ApiKeyMasker.Mask(LongKey);

        Assert.DoesNotContain("1234567890", masked, StringComparison.Ordinal);
        Assert.Equal(9, masked.Length);
    }

    [Fact]
    public void Redact_removes_every_occurrence_of_the_key()
    {
        var text = $"key={LongKey} and again {LongKey}";

        var redacted = ApiKeyMasker.Redact(text, LongKey);

        Assert.DoesNotContain(LongKey, redacted, StringComparison.Ordinal);
        Assert.Equal("key=gsk***nop and again gsk***nop", redacted);
    }

    [Fact]
    public void Redact_leaves_text_untouched_when_there_is_no_key()
    {
        Assert.Equal("plain details", ApiKeyMasker.Redact("plain details", null));
        Assert.Equal("plain details", ApiKeyMasker.Redact("plain details", "  "));
        Assert.Equal(string.Empty, ApiKeyMasker.Redact(null, LongKey));
    }

    [Fact]
    public void Engine_config_to_string_masks_the_key()
    {
        var config = new CloudEngineConfig(CloudProvider.Groq, LongKey, "Somning, Groq");

        var text = config.ToString();

        Assert.Contains("Provider = groq", text, StringComparison.Ordinal);
        Assert.Contains("gsk***nop", text, StringComparison.Ordinal);
        Assert.DoesNotContain(LongKey, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_config_masks_the_key_property()
    {
        var config = new CloudEngineConfig(CloudProvider.OpenAi, LongKey);

        Assert.Equal("gsk***nop", config.MaskedApiKey);
    }
}
