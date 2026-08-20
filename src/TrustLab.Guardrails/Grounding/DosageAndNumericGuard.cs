using System.Text.RegularExpressions;
using TrustLab.Domain.Models;

namespace TrustLab.Guardrails.Grounding;

public sealed record DosageVerificationResult(
    bool IsValid,
    IReadOnlyList<string> ExtractedDosagesInAnswer,
    IReadOnlyList<string> MissingDosages,
    string StatusMessage);

public static class DosageAndNumericGuard
{
    // Regex to match medical dosages: 500mg, 1000 mg, 10ml, 2x1, 5 mg/kg, 250 mcg, etc.
    private static readonly Regex DosagePattern = new(
        @"\b\d+(?:\.\d+)?\s*(?:mg|g|mcg|ml|cc|gr|mg\/kg|IU|iu|x\d+|tab|tablet|kapsül)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DosageVerificationResult VerifyDosages(string answer, IReadOnlyList<Chunk> sourceChunks)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new DosageVerificationResult(true, Array.Empty<string>(), Array.Empty<string>(), "No content to verify.");
        }

        var allContextText = string.Join(" ", sourceChunks.Select(c => c.Content));

        var answerDosages = DosagePattern.Matches(answer)
            .Select(m => NormalizeDosage(m.Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (answerDosages.Count == 0)
        {
            return new DosageVerificationResult(true, Array.Empty<string>(), Array.Empty<string>(), "No specific dosages found; verification skipped.");
        }

        var missingDosages = new List<string>();

        foreach (var dosage in answerDosages)
        {
            // Check if this dosage exists anywhere in source chunks
            if (!allContextText.Contains(dosage, StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(allContextText, Regex.Escape(dosage).Replace(@"\ ", @"\s*"), RegexOptions.IgnoreCase))
            {
                missingDosages.Add(dosage);
            }
        }

        if (missingDosages.Count > 0)
        {
            return new DosageVerificationResult(
                IsValid: false,
                ExtractedDosagesInAnswer: answerDosages,
                MissingDosages: missingDosages,
                StatusMessage: $"CRITICAL DOSAGE VIOLATION: The following dosage claims are NOT in the clinical source: {string.Join(", ", missingDosages)}");
        }

        return new DosageVerificationResult(
            IsValid: true,
            ExtractedDosagesInAnswer: answerDosages,
            MissingDosages: Array.Empty<string>(),
            StatusMessage: "All numerical dosages and units verified against source clinical documentation.");
    }

    private static string NormalizeDosage(string val)
    {
        return Regex.Replace(val.Trim().ToLowerInvariant(), @"\s+", "");
    }
}
