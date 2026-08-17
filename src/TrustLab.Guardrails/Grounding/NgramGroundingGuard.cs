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
        "by", "that", "this", "it", "from", "be", "are", "was", "were", "been", "has", "have", "had", "bu", "bir", "ve", "ile", "için", "olan", "olarak"
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

        // Aggregate source context stems and n-grams
        var contextText = string.Join(" ", sourceContext.Select(c => c.Content));
        var contextStems = Tokenizer.Tokenize(contextText)
            .Where(t => !StopWords.Contains(t))
            .Select(Tokenizer.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var contextBigrams = ExtractNgrams(contextText, 2);

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
            var sentenceStems = Tokenizer.Tokenize(sentence)
                .Where(t => !StopWords.Contains(t))
                .Select(Tokenizer.Stem)
                .ToList();

            if (sentenceStems.Count == 0)
            {
                groundedSentencesCount++;
                continue;
            }

            // 1. Unigram Stem Support Ratio
            int supportedUnigrams = sentenceStems.Count(t => contextStems.Contains(t));
            float unigramSupportRatio = (float)supportedUnigrams / sentenceStems.Count;

            // 2. Bigram Support
            var sentenceBigrams = ExtractNgrams(sentence, 2);
            int supportedBigrams = sentenceBigrams.Count(b => contextBigrams.Contains(b));
            float bigramSupportRatio = sentenceBigrams.Count > 0
                ? (float)supportedBigrams / sentenceBigrams.Count
                : unigramSupportRatio;

            // Factual sentence grounding score: 60% unigram + 40% bigram
            float sentenceGroundingScore = (unigramSupportRatio * 0.6f) + (bigramSupportRatio * 0.4f);

            // Strict grounding threshold per sentence (0.60)
            if (sentenceGroundingScore >= 0.60f)
            {
                groundedSentencesCount++;
            }
            else
            {
                violations.Add($"Ungrounded claim detected at sentence {i + 1} (Support: {sentenceGroundingScore:P1}): \"{sentence}\"");
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
