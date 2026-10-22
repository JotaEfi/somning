using System.Text.RegularExpressions;
namespace Somning.Core.Text;
public static partial class Wer
{
    [GeneratedRegex(@"[^\p{L}\p{N}\s]")] private static partial Regex Punctuation();
    public static string[] Tokens(string text) => Punctuation().Replace(text.ToLowerInvariant(), "")
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    public static double Compute(string expected, string actual)
    {
        var a = Tokens(expected); var b = Tokens(actual);
        if (a.Length == 0) return b.Length == 0 ? 0 : 1;
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1]; current[0] = i;
            for (int j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            previous = current;
        }
        return (double)previous[b.Length] / a.Length;
    }
}
