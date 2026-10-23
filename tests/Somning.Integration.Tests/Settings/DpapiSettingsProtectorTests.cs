using Somning.Platform.Windows.Settings;
using Xunit;

namespace Somning.Integration.Tests.Settings;

public sealed class DpapiSettingsProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTrip_PreservesSecret()
    {
        var protector = new DpapiSettingsProtector();
        const string secret = "sk-ant-api03-somning-test-token-123456789";

        var encrypted = protector.Protect(secret);
        Assert.NotEmpty(encrypted);
        Assert.NotEqual(secret, encrypted);

        var decrypted = protector.Unprotect(encrypted);
        Assert.Equal(secret, decrypted);
    }

    [Fact]
    public void EmptyOrInvalid_ReturnsEmptyString()
    {
        var protector = new DpapiSettingsProtector();

        Assert.Equal(string.Empty, protector.Protect(string.Empty));
        Assert.Equal(string.Empty, protector.Unprotect(string.Empty));
        Assert.Equal(string.Empty, protector.Unprotect("not-a-valid-base64-or-dpapi"));
    }
}
