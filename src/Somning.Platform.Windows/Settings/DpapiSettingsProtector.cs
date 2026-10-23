using System.Security.Cryptography;
using System.Text;
using Somning.Core.Settings;

namespace Somning.Platform.Windows.Settings;

public sealed class DpapiSettingsProtector : ISettingsProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Somning.ApiKey.Entropy.v1");

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(cipherBytes);
    }

    public string Unprotect(string cipherText)
    {
        if (string.IsNullOrWhiteSpace(cipherText))
            return string.Empty;

        try
        {
            var cipherBytes = Convert.FromBase64String(cipherText);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }
}
