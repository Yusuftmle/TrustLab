namespace TrustLab.Domain.Models;

public enum AgentWorkflowState
{
    Idle = 0,
    Ingesting = 1,
    Retrieving = 2,
    Synthesizing = 3,
    Validating = 4,
    Refining = 5,
    FallbackDispatched = 6,
    Completed = 7,
    Failed = 8
}

public sealed record AgentStepTrace(
    string StepName,
    AgentWorkflowState State,
    long ElapsedMilliseconds,
    IReadOnlyDictionary<string, object>? Metadata = null,
    string? Notes = null);

public sealed record EvaluationMetrics(
    float FaithfulnessScore,
    float ContextRecall,
    float ContextPrecision,
    float NoiseRejectionRatio,
    long LatencyMilliseconds,
    int TotalTokensUsed,
    bool PassedDeterministicGate);
