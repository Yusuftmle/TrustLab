namespace TrustLab.Guardrails.Evaluation;

public sealed record RankingMetrics(
    float PrecisionAtK,
    float RecallAtK,
    float Mrr,
    float NdcgAtK,
    int K);

public static class RankingMetricsCalculator
{
    public static RankingMetrics Calculate(
        IReadOnlyList<string> retrievedDocIds,
        IReadOnlySet<string> relevantDocIds,
        int k = 3)
    {
        if (retrievedDocIds.Count == 0 || relevantDocIds.Count == 0 || k <= 0)
        {
            return new RankingMetrics(0f, 0f, 0f, 0f, k);
        }

        var topKList = retrievedDocIds.Take(k).ToList();

        // 1. Precision@K = (Relevant retrieved in top K) / K
        int relevantInTopK = topKList.Count(id => relevantDocIds.Contains(id));
        float precisionAtK = (float)relevantInTopK / k;

        // 2. Recall@K = (Relevant retrieved in top K) / Total Relevant
        float recallAtK = (float)relevantInTopK / relevantDocIds.Count;

        // 3. MRR = 1 / Rank of first relevant document
        float mrr = 0f;
        for (int i = 0; i < retrievedDocIds.Count; i++)
        {
            if (relevantDocIds.Contains(retrievedDocIds[i]))
            {
                mrr = 1.0f / (i + 1);
                break;
            }
        }

        // 4. NDCG@K
        float dcg = 0f;
        for (int i = 0; i < topKList.Count; i++)
        {
            int rel = relevantDocIds.Contains(topKList[i]) ? 1 : 0;
            if (rel > 0)
            {
                dcg += (MathF.Pow(2, rel) - 1) / MathF.Log2(i + 2); // i+2 because 1-indexed rank = i+1
            }
        }

        float idcg = 0f;
        int idealCount = Math.Min(k, relevantDocIds.Count);
        for (int i = 0; i < idealCount; i++)
        {
            idcg += (MathF.Pow(2, 1) - 1) / MathF.Log2(i + 2);
        }

        float ndcgAtK = idcg > 0 ? dcg / idcg : 0f;

        return new RankingMetrics(
            (float)Math.Round(precisionAtK, 3),
            (float)Math.Round(recallAtK, 3),
            (float)Math.Round(mrr, 3),
            (float)Math.Round(ndcgAtK, 3),
            k);
    }
}
