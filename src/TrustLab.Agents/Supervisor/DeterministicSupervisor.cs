using System.Text;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;
using TrustLab.Telemetry;

namespace TrustLab.Agents.Supervisor;

public sealed class DeterministicSupervisor : IAgentSupervisor
{
    private readonly IHybridRetrievalPipeline _retrievalPipeline;
    private readonly ILlmClient _llmClient;
    private readonly IGroundingGuard _groundingGuard;
    private readonly ICircuitBreaker _circuitBreaker;

    public DeterministicSupervisor(
        IHybridRetrievalPipeline retrievalPipeline,
        ILlmClient llmClient,
        IGroundingGuard groundingGuard,
        ICircuitBreaker circuitBreaker)
    {
        _retrievalPipeline = retrievalPipeline ?? throw new ArgumentNullException(nameof(retrievalPipeline));
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _groundingGuard = groundingGuard ?? throw new ArgumentNullException(nameof(groundingGuard));
        _circuitBreaker = circuitBreaker ?? throw new ArgumentNullException(nameof(circuitBreaker));
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tracer = new ExecutionTracer();
        tracer.Start();

        tracer.RecordStep("Initialize", AgentWorkflowState.Idle);

        // 1. Context Retrieval
        tracer.RecordStep("RetrieveContext", AgentWorkflowState.Retrieving);
        var retrievalResults = await _retrievalPipeline.RetrieveAsync(
            request.Query,
            topK: 5,
            relevanceCutoff: request.MinimumRetrievalScore,
            cancellationToken: cancellationToken);

        var retrievedChunks = retrievalResults.Select(r => r.Chunk).ToList();

        if (retrievedChunks.Count == 0)
        {
            var deficitVerdict = GuardrailVerdict.Reject(
                ValidationFailureReason.ContextDeficit,
                new[] { "No relevant context found above minimum relevance threshold." });

            string fallback = _circuitBreaker.GetSafeFallbackResponse(request.Query, ValidationFailureReason.ContextDeficit);
            tracer.RecordStep("CircuitBreaker_ContextDeficit", AgentWorkflowState.FallbackDispatched);

            return new AgentExecutionResult(
                Query: request.Query,
                FinalOutput: fallback,
                IsSuccess: false,
                IsFallback: true,
                FinalState: AgentWorkflowState.FallbackDispatched,
                RetrievedContext: retrievedChunks,
                Traces: tracer.GetTraces(),
                FinalVerdict: deficitVerdict,
                RefinementAttempts: 0);
        }

        // 2. Closed-Loop Generation & Guardrail Refinement Loop
        int attempts = 0;
        string? candidateResponse = null;
        GuardrailVerdict lastVerdict = GuardrailVerdict.Reject(ValidationFailureReason.None, Array.Empty<string>());
        string? refinementFeedback = null;

        while (attempts < request.MaxRefinementAttempts)
        {
            attempts++;
            var currentState = attempts == 1 ? AgentWorkflowState.Synthesizing : AgentWorkflowState.Refining;
            tracer.RecordStep($"Generate_Attempt_{attempts}", currentState);

            string prompt = BuildPrompt(request.Query, retrievedChunks, refinementFeedback);
            candidateResponse = await _llmClient.GenerateResponseAsync(
                prompt,
                systemInstruction: "You are a deterministic factual engine. You must ONLY answer using the provided facts. Never extrapolate or add external knowledge.",
                temperature: 0.0f,
                cancellationToken: cancellationToken);

            // 3. Grounding Guard Verification
            tracer.RecordStep($"Validate_Attempt_{attempts}", AgentWorkflowState.Validating);
            lastVerdict = await _groundingGuard.VerifyGroundingAsync(
                candidateResponse,
                retrievedChunks,
                minimumFaithfulnessScore: request.MinimumFaithfulnessThreshold,
                cancellationToken: cancellationToken);

            if (lastVerdict.IsValid)
            {
                tracer.RecordStep("ValidationPassed", AgentWorkflowState.Completed);
                return new AgentExecutionResult(
                    Query: request.Query,
                    FinalOutput: lastVerdict.SanitizedOutput ?? candidateResponse,
                    IsSuccess: true,
                    IsFallback: false,
                    FinalState: AgentWorkflowState.Completed,
                    RetrievedContext: retrievedChunks,
                    Traces: tracer.GetTraces(),
                    FinalVerdict: lastVerdict,
                    RefinementAttempts: attempts);
            }

            // Prepare critic feedback for the next refinement pass
            refinementFeedback = $"PREVIOUS RESPONSE FAILED FACTUAL VALIDATION:\n" +
                                 string.Join("\n", lastVerdict.Violations.Select(v => $"- {v}")) +
                                 "\nStrictly remove all ungrounded claims and stick strictly to verified facts.";

            if (_circuitBreaker.ShouldTrip(lastVerdict, attempts))
            {
                break;
            }
        }

        // 4. Fallback dispatch when attempts exhausted or circuit breaker tripped
        string safeFallback = _circuitBreaker.GetSafeFallbackResponse(request.Query, lastVerdict.PrimaryFailureReason);
        tracer.RecordStep("CircuitBreaker_Tripped", AgentWorkflowState.FallbackDispatched);

        return new AgentExecutionResult(
            Query: request.Query,
            FinalOutput: safeFallback,
            IsSuccess: false,
            IsFallback: true,
            FinalState: AgentWorkflowState.FallbackDispatched,
            RetrievedContext: retrievedChunks,
            Traces: tracer.GetTraces(),
            FinalVerdict: lastVerdict,
            RefinementAttempts: attempts);
    }

    private static string BuildPrompt(string query, IReadOnlyList<Chunk> context, string? feedback)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### SOURCE CONTEXT DOCUMENTS:");
        for (int i = 0; i < context.Count; i++)
        {
            sb.AppendLine($"[DOC_{i + 1}]: {context[i].Content}");
        }

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(feedback))
        {
            sb.AppendLine("### CRITIC FEEDBACK FROM PREVIOUS ATTEMPT:");
            sb.AppendLine(feedback);
            sb.AppendLine();
        }

        sb.AppendLine("### USER QUERY:");
        sb.AppendLine(query);
        sb.AppendLine();
        sb.AppendLine("### FACTUAL ANSWER (Ground directly in DOCs):");

        return sb.ToString();
    }
}
