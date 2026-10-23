using System.Text;
using Somning.Core.Engine;

namespace Somning.Core.Text;

public sealed record PostFilterResult(bool Keep, string? Reason);

public static class PostFilter
{
    public static PostFilterResult Evaluate(string text, float maxNoSpeech, float noSpeechThreshold)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = Normalize(text);
        if (!normalized.Any(char.IsLetterOrDigit)) return new(false, "no_letters");
        if (HallucinationBlocklist.ContainsNormalized(normalized)) return new(false, "blocklist");
        if (maxNoSpeech > noSpeechThreshold) return new(false, "no_speech_prob");
        return new(true, null);
    }

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0) result.Append(' ');
                result.Append(character);
                pendingSpace = false;
            }
            else pendingSpace = result.Length > 0;
        }
        return result.ToString();
    }
}
