using System.Numerics.Tensors;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Rag.Indexing;

public sealed class DenseVectorStore : IVectorStore
{
    private readonly List<(Chunk Chunk, ReadOnlyMemory<float> Vector)> _entries = new();
    private readonly object _lock = new();

    public Task UpsertAsync(IReadOnlyList<(Chunk Chunk, ReadOnlyMemory<float> Vector)> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_lock)
        {
            foreach (var (chunk, vector) in entries)
            {
                int existingIndex = _entries.FindIndex(e => e.Chunk.Id == chunk.Id);
                if (existingIndex >= 0)
                {
                    _entries[existingIndex] = (chunk, vector);
                }
                else
                {
                    _entries.Add((chunk, vector));
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RetrievalResult>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK = 10, CancellationToken cancellationToken = default)
    {
        if (queryVector.IsEmpty || _entries.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RetrievalResult>>(Array.Empty<RetrievalResult>());
        }

        var results = new List<(Chunk Chunk, float Similarity)>();
        var querySpan = queryVector.Span;

        lock (_lock)
        {
            foreach (var (chunk, vector) in _entries)
            {
                if (vector.Length != querySpan.Length)
                {
                    continue;
                }

                // SIMD-accelerated Cosine Similarity via .NET TensorPrimitives
                float similarity = TensorPrimitives.CosineSimilarity(querySpan, vector.Span);
                if (!float.IsNaN(similarity))
                {
                    results.Add((chunk, similarity));
                }
            }
        }

        var ranked = results
            .OrderByDescending(r => r.Similarity)
            .Take(topK)
            .Select((r, index) => new RetrievalResult(r.Chunk, r.Similarity, "Dense_Cosine", index + 1))
            .ToList();

        return Task.FromResult<IReadOnlyList<RetrievalResult>>(ranked);
    }
}
