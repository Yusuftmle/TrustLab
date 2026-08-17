using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Rag.Indexing;

public sealed class Bm25SparseIndex : ISparseIndex
{
    private readonly double _k1;
    private readonly double _b;
    private readonly List<Chunk> _chunks = new();
    private readonly List<Dictionary<string, int>> _docTermFreqs = new();
    private readonly Dictionary<string, int> _docFrequencies = new(StringComparer.OrdinalIgnoreCase);
    private double _avgDocLength;
    private readonly object _lock = new();

    public Bm25SparseIndex(double k1 = 1.5, double b = 0.75)
    {
        _k1 = k1;
        _b = b;
    }

    public Task IndexChunksAsync(IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        lock (_lock)
        {
            _chunks.Clear();
            _docTermFreqs.Clear();
            _docFrequencies.Clear();

            _chunks.AddRange(chunks);
            long totalTokens = 0;

            foreach (var chunk in chunks)
            {
                var tokens = Tokenizer.Tokenize(chunk.Content);
                totalTokens += tokens.Count;

                var termFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var token in tokens)
                {
                    termFreq[token] = termFreq.TryGetValue(token, out int count) ? count + 1 : 1;
                }

                _docTermFreqs.Add(termFreq);

                foreach (var term in termFreq.Keys)
                {
                    _docFrequencies[term] = _docFrequencies.TryGetValue(term, out int df) ? df + 1 : 1;
                }
            }

            _avgDocLength = chunks.Count > 0 ? (double)totalTokens / chunks.Count : 0.0;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || _chunks.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        var queryTokens = Tokenizer.Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var results = new List<(Chunk Chunk, float Score)>();
        int n = _chunks.Count;

        lock (_lock)
        {
            for (int i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                var termFreqs = _docTermFreqs[i];
                int docLength = termFreqs.Values.Sum();
                double score = 0.0;

                foreach (var term in queryTokens)
                {
                    if (!termFreqs.TryGetValue(term, out int tf))
                    {
                        continue;
                    }

                    int df = _docFrequencies.TryGetValue(term, out int docFreq) ? docFreq : 0;
                    double idf = Math.Log(1.0 + (n - df + 0.5) / (df + 0.5));
                    if (idf < 0) idf = 0;

                    double numerator = tf * (_k1 + 1.0);
                    double denominator = tf + _k1 * (1.0 - _b + _b * (docLength / Math.Max(1.0, _avgDocLength)));

                    score += idf * (numerator / denominator);
                }

                if (score > 0.0)
                {
                    results.Add((chunk, (float)score));
                }
            }
        }

        var ranked = results
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .Select((r, index) => new RetrievalResult(r.Chunk, r.Score, "BM25_Sparse", index + 1))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(ranked);
    }
}
