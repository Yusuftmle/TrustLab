using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Application.Interfaces;

public interface ISchemaValidator
{
    Result<T> ValidateAndRepairJson<T>(string rawJsonOutput);
    Result<string> ValidateRawJsonSchema(string rawJsonOutput, string expectedJsonSchema);
}

public interface IGroundingGuard
{
    Task<GuardrailVerdict> VerifyGroundingAsync(
        string generatedResponse,
        IReadOnlyList<Chunk> sourceContext,
        float minimumFaithfulnessScore = 0.85f,
        CancellationToken cancellationToken = default);
}

public interface ICircuitBreaker
{
    bool ShouldTrip(GuardrailVerdict verdict, int consecutiveFailureCount);
    string GetSafeFallbackResponse(string query, ValidationFailureReason reason);
}

public interface ILlmClient
{
    Task<string> GenerateResponseAsync(
        string prompt,
        string? systemInstruction = null,
        float temperature = 0.0f,
        CancellationToken cancellationToken = default);
}

public sealed record AgentExecutionRequest(
    string Query,
    int MaxRefinementAttempts = 3,
    float MinimumFaithfulnessThreshold = 0.85f,
    float MinimumRetrievalScore = 0.35f);

public sealed record AgentExecutionResult(
    string Query,
    string FinalOutput,
    bool IsSuccess,
    bool IsFallback,
    AgentWorkflowState FinalState,
    IReadOnlyList<Chunk> RetrievedContext,
    IReadOnlyList<AgentStepTrace> Traces,
    GuardrailVerdict FinalVerdict,
    int RefinementAttempts);

public interface IAgentSupervisor
{
    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IEvaluationService
{
    EvaluationMetrics EvaluateExecution(
        AgentExecutionResult result,
        string? groundTruthAnswer = null,
        IReadOnlyList<string>? expectedSourceChunkIds = null);
}
