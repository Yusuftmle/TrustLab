using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Rag.Reranking;

public sealed class CrossEncoderReranker : IReranker
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "in", "with", "to", "for", "of", "as",
        "by", "that", "this", "it", "from", "be", "are", "was", "were", "been", "has", "have", "had",
        "how", "what", "where", "when", "why", "who", "does", "do", "did"
    };

    public Task<IReadOnlyList<RetrievalResult>> RerankAsync(
        string query,
        IReadOnlyList<RetrievalResult> candidates,
        float minimumRelevanceThreshold = 0.25f,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        var rawQueryTokens = Tokenizer.Tokenize(query);
        var contentQueryTokens = rawQueryTokens
            .Where(t => !StopWords.Contains(t))
            .Select(Tokenizer.Stem)
            .ToList();

        if (contentQueryTokens.Count == 0)
        {
            contentQueryTokens = rawQueryTokens.Select(Tokenizer.Stem).ToList();
        }

        var scored = new List<(RetrievalResult Candidate, float RerankScore)>();

        foreach (var candidate in candidates)
        {
            var rawDocTokens = Tokenizer.Tokenize(candidate.Chunk.Content);
            var docStems = rawDocTokens.Select(Tokenizer.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (docStems.Count == 0)
            {
                continue;
            }

            // 1. Query Term Coverage (stemmed lexical overlap)
            int matchedQueryTerms = contentQueryTokens.Count(qt => docStems.Contains(qt));
            float queryCoverage = (float)matchedQueryTerms / contentQueryTokens.Count;

            // 2. Sequential Bigram Co-occurrence
            float phraseAffinity = CalculateBigramAffinity(rawQueryTokens, rawDocTokens);

            // 3. Reciprocal Rank Prior Signal
            float rankSignal = candidate.Rank.HasValue && candidate.Rank.Value > 0
                ? (1.0f / candidate.Rank.Value)
                : Math.Clamp(candidate.Score, 0.0f, 1.0f);

            // Rerank score: 55% coverage + 25% phrase affinity + 20% prior rank signal
            float finalRerankScore = (queryCoverage * 0.55f) + (phraseAffinity * 0.25f) + (rankSignal * 0.20f);

            if (finalRerankScore >= minimumRelevanceThreshold)
            {
                scored.Add((candidate, finalRerankScore));
            }
        }

        var reranked = scored
            .OrderByDescending(s => s.RerankScore)
            .Take(topK)
            .Select((s, idx) => new RetrievalResult(
                Chunk: s.Candidate.Chunk,
                Score: s.RerankScore,
                RetrievalType: "CrossEncoder_Reranked",
                Rank: idx + 1))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(reranked);
    }

    private static float CalculateBigramAffinity(IReadOnlyList<string> queryTokens, IReadOnlyList<string> docTokens)
    {
        if (queryTokens.Count < 2 || docTokens.Count < 2)
        {
            return 0.0f;
        }

        var queryBigrams = new HashSet<string>();
        for (int i = 0; i < queryTokens.Count - 1; i++)
        {
            queryBigrams.Add($"{Tokenizer.Stem(queryTokens[i])}_{Tokenizer.Stem(queryTokens[i + 1])}");
        }

        int matches = 0;
        for (int i = 0; i < docTokens.Count - 1; i++)
        {
            string docBigram = $"{Tokenizer.Stem(docTokens[i])}_{Tokenizer.Stem(docTokens[i + 1])}";
            if (queryBigrams.Contains(docBigram))
            {
                matches++;
            }
        }

        return Math.Min(1.0f, (float)matches / queryBigrams.Count);
    }
}
