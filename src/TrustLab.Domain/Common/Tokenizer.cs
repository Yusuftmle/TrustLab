using System.Text.RegularExpressions;

namespace TrustLab.Domain.Common;

public static class Tokenizer
{
    private static readonly Regex TokenRegex = new(@"\b[\w\-]{2,}\b", RegexOptions.Compiled);

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
            return word;
        }

        string lower = word.ToLowerInvariant();

        if (lower.EndsWith("sses")) return lower[..^2];
        if (lower.EndsWith("ies")) return lower[..^3] + "y";
        if (lower.EndsWith("ing") && lower.Length > 5) return lower[..^3];
        if (lower.EndsWith("ed") && lower.Length > 4) return lower[..^2];
        if (lower.EndsWith("s") && !lower.EndsWith("ss")) return lower[..^1];

        return lower;
    }

    public static IReadOnlyList<string> TokenizeAndStem(string text)
    {
        return Tokenize(text).Select(Stem).ToList();
    }
}
