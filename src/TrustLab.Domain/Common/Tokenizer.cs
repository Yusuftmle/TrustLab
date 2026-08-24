using System.Text.RegularExpressions;

namespace TrustLab.Domain.Common;

public static class Tokenizer
{
    private static readonly Regex TokenRegex = new(@"\b[\w\-]{2,}\b", RegexOptions.Compiled);

    // Türkçe çekim ekleri (en uzundan en kısaya doğru)
    private static readonly string[] TurkishSuffixes = new[]
    {
        // 4-5 harfli ekler
        "lerinin", "larının", "lerinden", "larından", "lerindeki", "larındaki",
        "mektedir", "maktadır", "memektedir", "mamaktadır",
        "kontrendikedir", "kontrendike",
        // 3 harfli ekler
        "leri", "ları", "inde", "ında", "inde", "ında", "kten", "ktan",
        "nden", "ndan", "den", "dan", "ten", "tan",
        "dir", "dır", "dur", "dür", "tir", "tır", "tur", "tür",
        "miş", "mış", "muş", "müş", "nin", "nın", "nun", "nün",
        "siz", "sız", "suz", "süz", "lik", "lık", "luk", "lük",
        // 2 harfli ekler
        "ler", "lar", "de", "da", "te", "ta", "ye", "ya", "le", "la",
        "in", "ın", "un", "ün", "si", "sı", "su", "sü", "yi", "yı", "yu", "yü",
        "li", "lı", "lu", "lü", "ci", "cı", "cu", "cü", "ce", "ca"
    };

    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return TokenRegex.Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .ToList();
    }

    public static string Stem(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Length <= 3)
        {
            return word.ToLowerInvariant();
        }

        string lower = word.ToLowerInvariant();

        // 1. Türkçe Çekim Ekleri Temizleme (kullanımının -> kullanım, hastalarda -> hasta vb.)
        foreach (var suffix in TurkishSuffixes)
        {
            if (lower.EndsWith(suffix) && (lower.Length - suffix.Length) >= 3)
            {
                lower = lower[..^suffix.Length];
                break;
            }
        }

        // 2. İngilizce Ekler
        if (lower.EndsWith("sses")) return lower[..^2];
        if (lower.EndsWith("ies")) return lower[..^3] + "y";
        if (lower.EndsWith("ing") && lower.Length > 5) return lower[..^3];
        if (lower.EndsWith("ed") && lower.Length > 4) return lower[..^2];
        if (lower.EndsWith("s") && !lower.EndsWith("ss")) return lower[..^1];

        return lower;
    }

    /// <summary>
    /// İki Türkçe kelimenin kök veya önek benzerliğini kontrol eder (örn: kontrendikasyon vs kontrendikedir)
    /// </summary>
    public static bool IsFuzzyStemMatch(string w1, string w2)
    {
        if (string.Equals(w1, w2, StringComparison.OrdinalIgnoreCase)) return true;

        string s1 = Stem(w1);
        string s2 = Stem(w2);

        if (string.Equals(s1, s2, StringComparison.OrdinalIgnoreCase)) return true;

        // 4+ harfli ortak önek kontrolü
        if (s1.Length >= 4 && s2.Length >= 4)
        {
            int minLen = Math.Min(s1.Length, s2.Length);
            int prefixLen = Math.Min(minLen, 5);
            if (s1[..prefixLen].Equals(s2[..prefixLen], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> TokenizeAndStem(string text)
    {
        return Tokenize(text).Select(Stem).ToList();
    }
}
