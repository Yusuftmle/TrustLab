using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Agents.Evaluation;

public sealed class DeterministicEvaluationService : IEvaluationService
{
    public EvaluationMetrics EvaluateExecution(
        AgentExecutionResult result,
        string? groundTruthAnswer = null,
        IReadOnlyList<string>? expectedSourceChunkIds = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        // 1. Faithfulness Score
        float faithfulness = result.FinalVerdict.FaithfulnessScore;

        // 2. Context Recall
        float contextRecall = 1.0f;
        if (expectedSourceChunkIds != null && expectedSourceChunkIds.Count > 0)
        {
            var retrievedIds = result.RetrievedContext.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int matched = expectedSourceChunkIds.Count(id => retrievedIds.Contains(id));
            contextRecall = (float)matched / expectedSourceChunkIds.Count;
        }

        // 3. Context Precision
        float contextPrecision = 1.0f;
        if (expectedSourceChunkIds != null && expectedSourceChunkIds.Count > 0 && result.RetrievedContext.Count > 0)
        {
            var expectedSet = expectedSourceChunkIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            int relevantRetrieved = result.RetrievedContext.Count(c => expectedSet.Contains(c.Id));
            contextPrecision = (float)relevantRetrieved / result.RetrievedContext.Count;
        }

        // 4. Noise Rejection Ratio
        float noiseRejectionRatio = result.RetrievedContext.Count == 0 ? 1.0f : Math.Clamp(contextPrecision, 0.0f, 1.0f);

        // 5. Total Latency
        long totalLatency = result.Traces.Count > 0 ? result.Traces[^1].ElapsedMilliseconds : 0;

        // 6. Token count estimation
        int totalTokens = (result.Query.Length + result.FinalOutput.Length + result.RetrievedContext.Sum(c => c.Content.Length)) / 4;

        // 7. Gate decision: Must be valid, zero ungrounded violations, and not fallback when truth exists
        bool passedGate = result.IsSuccess && faithfulness >= 0.85f && !result.IsFallback;

        return new EvaluationMetrics(
            FaithfulnessScore: faithfulness,
            ContextRecall: contextRecall,
            ContextPrecision: contextPrecision,
            NoiseRejectionRatio: noiseRejectionRatio,
            LatencyMilliseconds: totalLatency,
            TotalTokensUsed: totalTokens,
            PassedDeterministicGate: passedGate);
    }
}
