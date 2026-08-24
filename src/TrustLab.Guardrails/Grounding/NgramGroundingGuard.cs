using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Guardrails.Grounding;

public sealed class NgramGroundingGuard : IGroundingGuard
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "in", "with", "to", "for", "of", "as",
        "by", "that", "this", "it", "from", "be", "are", "was", "were", "been", "has", "have", "had",
        "bu", "bir", "ve", "ile", "için", "olan", "olarak", "veya", "ya", "da", "de", "ki", "ancak",
        "fakat", "ise", "gibi", "göre", "kadar", "çok", "daha", "en", "her", "tüm", "bazı", "mı", "mi",
        "mu", "mü", "var", "yok", "hakkında", "başka", "durum", "olduğu", "olduğunu", "yani", "tarafından"
    };

    public Task<GuardrailVerdict> VerifyGroundingAsync(
        string generatedResponse,
        IReadOnlyList<Chunk> sourceContext,
        float minimumFaithfulnessScore = 0.80f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(generatedResponse))
        {
            return Task.FromResult(GuardrailVerdict.Reject(
                ValidationFailureReason.UngroundedClaim,
                new[] { "Generated response is empty." },
                faithfulnessScore: 0.0f));
        }

        if (sourceContext == null || sourceContext.Count == 0)
        {
            return Task.FromResult(GuardrailVerdict.Reject(
                ValidationFailureReason.ContextDeficit,
                new[] { "No source context chunks provided for grounding verification." },
                faithfulnessScore: 0.0f));
        }

        // Aggregate source context tokens
        var allContextTokens = sourceContext
            .SelectMany(c => Tokenizer.Tokenize(c.Content))
            .Where(t => !StopWords.Contains(t))
            .ToList();

        // Split generated response into sentences/claims
        var sentences = SplitIntoSentences(generatedResponse);
        if (sentences.Count == 0)
        {
            return Task.FromResult(GuardrailVerdict.Pass(1.0f, generatedResponse));
        }

        var violations = new List<string>();
        int groundedSentencesCount = 0;

        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];

            var sTokens = Tokenizer.Tokenize(sentence)
                .Where(t => !StopWords.Contains(t))
                .ToList();

            // Kısa soru/nezaket cümlesi veya içerik kelimesi içermeyen bağlaç cümleleri
            if (sTokens.Count <= 2 && (sentence.EndsWith("?") || sentence.Length < 35))
            {
                groundedSentencesCount++;
                continue;
            }

            if (sTokens.Count == 0)
            {
                groundedSentencesCount++;
                continue;
            }

            // Türkçe kök eşleşmesi
            int supportedTokens = sTokens.Count(st => allContextTokens.Any(ct => Tokenizer.IsFuzzyStemMatch(st, ct)));
            float tokenSupportRatio = (float)supportedTokens / sTokens.Count;

            if (tokenSupportRatio >= 0.40f)
            {
                groundedSentencesCount++;
            }
            else
            {
                violations.Add($"Ungrounded claim detected at sentence {i + 1} (Support: {tokenSupportRatio:P1}): \"{sentence}\"");
            }
        }

        float overallFaithfulnessScore = (float)groundedSentencesCount / sentences.Count;

        var telemetry = new Dictionary<string, object>
        {
            ["total_sentences"] = sentences.Count,
            ["grounded_sentences"] = groundedSentencesCount,
            ["faithfulness_score"] = overallFaithfulnessScore,
            ["violations_count"] = violations.Count
        };

        if (overallFaithfulnessScore >= minimumFaithfulnessScore && violations.Count == 0)
        {
            return Task.FromResult(GuardrailVerdict.Pass(
                faithfulnessScore: overallFaithfulnessScore,
                sanitizedOutput: generatedResponse,
                telemetry: telemetry));
        }

        var primaryReason = violations.Count > 0
            ? ValidationFailureReason.UngroundedClaim
            : ValidationFailureReason.ConfidenceBelowThreshold;

        return Task.FromResult(GuardrailVerdict.Reject(
            primaryReason,
            violations,
            faithfulnessScore: overallFaithfulnessScore,
            telemetry: telemetry));
    }

    private static IReadOnlyList<string> SplitIntoSentences(string text)
    {
        return Regex.Split(text, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static HashSet<string> ExtractNgrams(string text, int n)
    {
        var tokens = Tokenizer.Tokenize(text)
            .Where(t => !StopWords.Contains(t))
            .Select(Tokenizer.Stem)
            .ToList();

        var ngrams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tokens.Count < n)
        {
            return ngrams;
        }

        for (int i = 0; i <= tokens.Count - n; i++)
        {
            ngrams.Add(string.Join("_", tokens.Skip(i).Take(n)));
        }

        return ngrams;
    }
}
