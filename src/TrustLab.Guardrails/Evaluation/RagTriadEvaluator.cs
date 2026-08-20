using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Guardrails.Evaluation;

public sealed record RagTriadScore(
    float ContextRelevancy,
    float Faithfulness,
    float AnswerRelevancy,
    IReadOnlyList<SentenceGroundingDetail> SentenceDetails);

public sealed record SentenceGroundingDetail(
    int SentenceIndex,
    string Sentence,
    bool IsGrounded,
    float SupportRatio,
    string? BestMatchingDocId,
    string? BestMatchingSnippet);

public sealed class RagTriadEvaluator
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "in", "with", "to", "for", "of", "as",
        "by", "that", "this", "it", "from", "be", "are", "was", "were", "been", "has", "have", "had", "bu", "bir", "ve", "ile", "için", "olan", "olarak"
    };

    public RagTriadScore Evaluate(
        string query,
        IReadOnlyList<Chunk> retrievedChunks,
        string generatedAnswer,
        ReadOnlyMemory<float>? queryVector = null,
        ReadOnlyMemory<float>? answerVector = null)
    {
        // 1. Context Relevancy: How much of retrieved context is relevant to query?
        float contextRelevancy = CalculateContextRelevancy(query, retrievedChunks);

        // 2. Faithfulness: How many claims in generated answer are backed by retrieved context?
        var (faithfulness, sentenceDetails) = CalculateFaithfulness(generatedAnswer, retrievedChunks);

        // 3. Answer Relevancy: Semantic alignment between query and generated answer
        float answerRelevancy = CalculateAnswerRelevancy(query, generatedAnswer, queryVector, answerVector);

        return new RagTriadScore(
            (float)Math.Round(contextRelevancy, 3),
            (float)Math.Round(faithfulness, 3),
            (float)Math.Round(answerRelevancy, 3),
            sentenceDetails);
    }

    private static float CalculateContextRelevancy(string query, IReadOnlyList<Chunk> chunks)
    {
        if (chunks.Count == 0 || string.IsNullOrWhiteSpace(query)) return 0f;

        var queryStems = Tokenizer.Tokenize(query)
            .Where(t => !StopWords.Contains(t))
            .Select(Tokenizer.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (queryStems.Count == 0) return 1f;

        int totalSentences = 0;
        int relevantSentences = 0;

        foreach (var chunk in chunks)
        {
            var chunkSentences = Regex.Split(chunk.Content, @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var cs in chunkSentences)
            {
                totalSentences++;
                var csStems = Tokenizer.Tokenize(cs)
                    .Where(t => !StopWords.Contains(t))
                    .Select(Tokenizer.Stem)
                    .ToList();

                if (csStems.Count > 0 && csStems.Any(s => queryStems.Contains(s)))
                {
                    relevantSentences++;
                }
            }
        }

        return totalSentences > 0 ? (float)relevantSentences / totalSentences : 0f;
    }

    private static (float Faithfulness, IReadOnlyList<SentenceGroundingDetail> Details) CalculateFaithfulness(
        string answer,
        IReadOnlyList<Chunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(answer)) return (0f, Array.Empty<SentenceGroundingDetail>());
        if (chunks.Count == 0) return (0f, Array.Empty<SentenceGroundingDetail>());

        var sentences = Regex.Split(answer, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (sentences.Count == 0) return (1f, Array.Empty<SentenceGroundingDetail>());

        var allContextText = string.Join(" ", chunks.Select(c => c.Content));
        var contextStems = Tokenizer.Tokenize(allContextText)
            .Where(t => !StopWords.Contains(t))
            .Select(Tokenizer.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var details = new List<SentenceGroundingDetail>();
        int groundedCount = 0;

        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            var sStems = Tokenizer.Tokenize(sentence)
                .Where(t => !StopWords.Contains(t))
                .Select(Tokenizer.Stem)
                .ToList();

            if (sStems.Count == 0)
            {
                groundedCount++;
                details.Add(new SentenceGroundingDetail(i + 1, sentence, true, 1.0f, null, null));
                continue;
            }

            int matched = sStems.Count(s => contextStems.Contains(s));
            float supportRatio = (float)matched / sStems.Count;
            bool isGrounded = supportRatio >= 0.50f;

            if (isGrounded) groundedCount++;

            // Find best matching source chunk
            string? bestDocId = null;
            string? bestSnippet = null;
            int maxDocMatches = 0;

            foreach (var chunk in chunks)
            {
                var docStems = Tokenizer.Tokenize(chunk.Content)
                    .Where(t => !StopWords.Contains(t))
                    .Select(Tokenizer.Stem)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                int docMatches = sStems.Count(s => docStems.Contains(s));
                if (docMatches > maxDocMatches)
                {
                    maxDocMatches = docMatches;
                    bestDocId = chunk.DocumentId;
                    bestSnippet = chunk.Content.Length > 80 ? chunk.Content[..80] + "..." : chunk.Content;
                }
            }

            details.Add(new SentenceGroundingDetail(
                SentenceIndex: i + 1,
                Sentence: sentence,
                IsGrounded: isGrounded,
                SupportRatio: (float)Math.Round(supportRatio, 2),
                BestMatchingDocId: bestDocId,
                BestMatchingSnippet: bestSnippet));
        }

        float faithfulness = (float)groundedCount / sentences.Count;
        return (faithfulness, details);
    }

    private static float CalculateAnswerRelevancy(string query, string answer, ReadOnlyMemory<float>? qVec, ReadOnlyMemory<float>? aVec)
    {
        if (qVec.HasValue && aVec.HasValue && qVec.Value.Length == aVec.Value.Length && qVec.Value.Length > 0)
        {
            var spanQ = qVec.Value.Span;
            var spanA = aVec.Value.Span;
            float dot = 0;
            for (int i = 0; i < spanQ.Length; i++) dot += spanQ[i] * spanA[i];
            return Math.Clamp(dot, 0f, 1f);
        }

        // Fallback lexical jaccard overlap if vectors not supplied
        var qTokens = Tokenizer.Tokenize(query).Where(t => !StopWords.Contains(t)).Select(Tokenizer.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aTokens = Tokenizer.Tokenize(answer).Where(t => !StopWords.Contains(t)).Select(Tokenizer.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (qTokens.Count == 0 || aTokens.Count == 0) return 0f;

        int intersection = qTokens.Count(t => aTokens.Contains(t));
        int union = qTokens.Union(aTokens, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (float)intersection / union : 0f;
    }
}
