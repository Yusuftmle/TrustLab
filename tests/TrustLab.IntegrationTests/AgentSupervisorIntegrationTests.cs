using FluentAssertions;
using TrustLab.Agents.Evaluation;
using TrustLab.Agents.Supervisor;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;
using TrustLab.Guardrails.CircuitBreaker;
using TrustLab.Guardrails.Grounding;
using TrustLab.Infrastructure.Embedding;
using TrustLab.Infrastructure.Llm;
using TrustLab.Rag.Chunking;
using TrustLab.Rag.Indexing;
using TrustLab.Rag.Pipeline;
using TrustLab.Rag.Reranking;
using Xunit;

namespace TrustLab.IntegrationTests;

public class AgentSupervisorIntegrationTests
{
    private readonly IHybridRetrievalPipeline _pipeline;
    private readonly IGroundingGuard _groundingGuard;
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly IEvaluationService _evaluator;

    public AgentSupervisorIntegrationTests()
    {
        var chunker = new SemanticBoundaryChunker();
        var sparse = new Bm25SparseIndex();
        var dense = new DenseVectorStore();
        var embedder = new DeterministicHashEmbedder(64);
        var reranker = new CrossEncoderReranker();

        _pipeline = new HybridRetrievalPipeline(chunker, sparse, dense, embedder, reranker);
        _groundingGuard = new NgramGroundingGuard();
        _circuitBreaker = new DeterministicCircuitBreaker(maxConsecutiveFailures: 2);
        _evaluator = new DeterministicEvaluationService();
    }

    private async Task SeedKnowledgeBaseAsync()
    {
        var docs = new List<Document>
        {
            Document.Create(
                "doc_guardrail",
                "TrustLab architecture implements deterministic guardrails in C# .NET 9. It uses Reciprocal Rank Fusion and SIMD TensorPrimitives for zero hallucination RAG."),
            Document.Create(
                "doc_telemetry",
                "ExecutionTracer monitors latency, tokens, and step-by-step verification gates to ensure strict auditability.")
        };

        await _pipeline.IndexAsync(docs);
    }

    [Fact]
    public async Task Supervisor_ShouldSucceed_WhenResponseIsGrounded()
    {
        // Arrange
        await SeedKnowledgeBaseAsync();
        var llm = MockDeterministicLlmClient.CreateGrounded(
            "TrustLab architecture implements deterministic guardrails in C# .NET 9 with Reciprocal Rank Fusion.");
        var supervisor = new DeterministicSupervisor(_pipeline, llm, _groundingGuard, _circuitBreaker);

        var request = new AgentExecutionRequest(Query: "How does TrustLab implement guardrails?");

        // Act
        var result = await supervisor.ExecuteAsync(request);
        var eval = _evaluator.EvaluateExecution(result);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.IsFallback.Should().BeFalse();
        result.FinalState.Should().Be(AgentWorkflowState.Completed);
        result.RefinementAttempts.Should().Be(1);
        eval.PassedDeterministicGate.Should().BeTrue();
        eval.FaithfulnessScore.Should().BeGreaterThanOrEqualTo(0.85f);
    }

    [Fact]
    public async Task Supervisor_ShouldSelfCorrect_WhenInitialResponseHallucinates()
    {
        // Arrange
        await SeedKnowledgeBaseAsync();
        var llm = MockDeterministicLlmClient.CreateSelfCorrecting(
            initialHallucination: "TrustLab uses Python Flask with MongoDB backend.",
            correctedResponse: "TrustLab architecture implements deterministic guardrails in C# .NET 9.");
        var supervisor = new DeterministicSupervisor(_pipeline, llm, _groundingGuard, _circuitBreaker);

        var request = new AgentExecutionRequest(Query: "What is the architecture of TrustLab?");

        // Act
        var result = await supervisor.ExecuteAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RefinementAttempts.Should().Be(2);
        result.FinalState.Should().Be(AgentWorkflowState.Completed);
        result.FinalVerdict.IsValid.Should().BeTrue();
        llm.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Supervisor_ShouldTripCircuitBreaker_WhenPersistentHallucinationOccurs()
    {
        // Arrange
        await SeedKnowledgeBaseAsync();
        var llm = MockDeterministicLlmClient.CreatePersistentHallucinator(
            "Persistent hallucination: TrustLab is written in Fortran 77.");
        var supervisor = new DeterministicSupervisor(_pipeline, llm, _groundingGuard, _circuitBreaker);

        var request = new AgentExecutionRequest(
            Query: "What language is TrustLab written in?",
            MaxRefinementAttempts: 2);

        // Act
        var result = await supervisor.ExecuteAsync(request);
        var eval = _evaluator.EvaluateExecution(result);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.IsFallback.Should().BeTrue();
        result.FinalState.Should().Be(AgentWorkflowState.FallbackDispatched);
        result.FinalOutput.Should().Contain("unable to provide a verified answer");
        eval.PassedDeterministicGate.Should().BeFalse();
    }

    [Fact]
    public async Task Supervisor_ShouldTripImmediately_OnContextDeficit()
    {
        // Arrange
        await SeedKnowledgeBaseAsync();
        var llm = MockDeterministicLlmClient.CreateGrounded("Irrelevant");
        var supervisor = new DeterministicSupervisor(_pipeline, llm, _groundingGuard, _circuitBreaker);

        // Query completely unrelated to seeded knowledge
        var request = new AgentExecutionRequest(
            Query: "Quantum mechanics of superheated black holes in deep space",
            MinimumRetrievalScore: 0.60f);

        // Act
        var result = await supervisor.ExecuteAsync(request);

        // Assert
        result.IsFallback.Should().BeTrue();
        result.FinalState.Should().Be(AgentWorkflowState.FallbackDispatched);
        result.FinalOutput.Should().Contain("lacks sufficient factual context");
    }
}
