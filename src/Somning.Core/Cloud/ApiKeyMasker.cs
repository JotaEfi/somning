namespace Somning.Core.Cloud;

/// <summary>
/// Produces the only representation of an API key that may reach logs, diagnostics or exception
/// messages (APP-06): <c>gsk***abc</c> for long keys, <c>***</c> for short ones.
/// </summary>
public static class ApiKeyMasker
{
    /// <summary>The fixed placeholder that replaces the secret portion of a key.</summary>
    public const string MaskToken = "***";

    private const int VisibleChars = 3;
    private const int MinLengthForPartialReveal = 9;

    /// <summary>
    /// Masks <paramref name="key"/>: keys longer than 8 characters keep their first and last 3
    /// characters, everything else becomes <see cref="MaskToken"/>. Null/blank yields an empty string.
    /// </summary>
    public static string Mask(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        if (key.Length < MinLengthForPartialReveal) return MaskToken;
        return string.Concat(key.AsSpan(0, VisibleChars), MaskToken, key.AsSpan(key.Length - VisibleChars));
    }

    /// <summary>Removes every occurrence of <paramref name="key"/> from text that is about to be surfaced.</summary>
    public static string Redact(string? text, string? key)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return text;
        return text.Replace(key, Mask(key), StringComparison.Ordinal);
    }
}
