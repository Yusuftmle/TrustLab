using TrustLab.Domain.Models;

namespace TrustLab.Rag.Fusion;

public sealed class ReciprocalRankFusion
{
    private readonly int _k;

    public ReciprocalRankFusion(int k = 60)
    {
        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(k), "RRF constant k must be greater than 0.");
        }
        _k = k;
    }

    public IReadOnlyList<RetrievalResult> Fuse(
        IReadOnlyList<(IReadOnlyList<RetrievalResult> Results, float Weight)> rankedLists,
        int topK = 10)
    {
        if (rankedLists == null || rankedLists.Count == 0)
        {
            return Array.Empty<RetrievalResult>();
        }

        var chunkMap = new Dictionary<string, Chunk>();
        var rrfScores = new Dictionary<string, float>();

        foreach (var (results, weight) in rankedLists)
        {
            for (int rank = 0; rank < results.Count; rank++)
            {
                var item = results[rank];
                string chunkId = item.Chunk.Id;

                if (!chunkMap.ContainsKey(chunkId))
                {
                    chunkMap[chunkId] = item.Chunk;
                }

                // RRF formula: weight / (k + rank)
                float contribution = weight / (_k + (rank + 1));
                rrfScores[chunkId] = rrfScores.TryGetValue(chunkId, out float current)
                    ? current + contribution
                    : contribution;
            }
        }

        var fused = rrfScores
            .OrderByDescending(kv => kv.Value)
            .Take(topK)
            .Select((kv, index) => new RetrievalResult(
                Chunk: chunkMap[kv.Key],
                Score: kv.Value,
                RetrievalType: "Hybrid_RRF",
                Rank: index + 1))
            .ToList();

        return fused;
    }
}
