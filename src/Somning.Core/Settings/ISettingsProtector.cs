namespace Somning.Core.Settings;

public interface ISettingsProtector
{
    string Protect(string plainText);
    string Unprotect(string cipherText);
}
