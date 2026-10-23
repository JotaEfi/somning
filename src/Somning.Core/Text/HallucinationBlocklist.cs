namespace Somning.Core.Text;

public static class HallucinationBlocklist
{
    private static readonly string[] entries =
    [
        "Obrigado.", "Obrigada.", "Obrigado por assistir!", "Obrigada por assistir!",
        "Legendas pela comunidade Amara.org", "Amara.org", "Inscreva-se",
        "Inscreva-se no canal.", "Aplausos.", "[Música]", "Legenda por Sônia Ruberti",
        "Legendas por Sônia Ruberti"
    ];

    private static readonly HashSet<string> normalizedEntries =
        entries.Select(PostFilter.Normalize).ToHashSet(StringComparer.Ordinal);

    public static bool ContainsNormalized(string normalized) =>
        normalizedEntries.Contains(normalized);
}
