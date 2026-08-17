using System.Diagnostics;
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

Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("===============================================================================");
Console.WriteLine("  TRUSTLAB (.NET 9) DETERMINISTIC RAG & GUARDRAIL BENCHMARK HARNESS");
Console.WriteLine("===============================================================================");
Console.ResetColor();

// 1. Initialize Clean Architecture components
var chunker = new SemanticBoundaryChunker();
var sparseIndex = new Bm25SparseIndex();
var vectorStore = new DenseVectorStore();
var embedder = new DeterministicHashEmbedder(dimensions: 128);
var reranker = new CrossEncoderReranker();
var pipeline = new HybridRetrievalPipeline(chunker, sparseIndex, vectorStore, embedder, reranker);
var groundingGuard = new NgramGroundingGuard();
var circuitBreaker = new DeterministicCircuitBreaker(maxConsecutiveFailures: 2);
var evaluator = new DeterministicEvaluationService();

// 2. Ingest Multi-Domain Benchmark Corpus
Console.WriteLine("\n[1/3] Ingesting & Indexing Benchmark Knowledge Base...");
var sw = Stopwatch.StartNew();

var corpus = new List<Document>
{
    Document.Create("doc_rag_01", "TrustLab Hybrid Search fuses Okapi BM25 sparse index and Dense SIMD Cosine Similarity via Reciprocal Rank Fusion (RRF) with constant k=60."),
    Document.Create("doc_guard_02", "Deterministic guardrails validate LLM outputs against strict schemas and execute n-gram grounding verification to eliminate hallucinations."),
    Document.Create("doc_circuit_03", "The deterministic circuit breaker trips when consecutive grounding checks fail or when retrieved context exhibits severe deficit."),
    Document.Create("doc_telemetry_04", "ExecutionTracer measures nanosecond and millisecond latencies, tracking state machine transitions from Ingesting to Completed."),
    Document.Create("doc_distractor_05", "Photosynthesis in plants converts carbon dioxide and sunlight into glucose and oxygen through light-dependent reactions.")
};

await pipeline.IndexAsync(corpus);
sw.Stop();
Console.WriteLine($"[+] Indexed {corpus.Count} documents across BM25 & SIMD Vector Store in {sw.ElapsedMilliseconds} ms.");

// 3. Define Benchmark Test Matrix
var benchmarkCases = new[]
{
    new
    {
        Name = "TC-01: Factual Retrieval & Synthesis",
        Query = "How does TrustLab Hybrid Search fuse rankings?",
        ExpectedChunk = (string?)"doc_rag_01_c0",
        LlmType = "grounded",
        MockAnswer = "TrustLab Hybrid Search fuses Okapi BM25 sparse index and Dense SIMD Cosine Similarity via Reciprocal Rank Fusion with constant k=60."
    },
    new
    {
        Name = "TC-02: Self-Correction on Initial Hallucination",
        Query = "What is the function of the deterministic circuit breaker?",
        ExpectedChunk = (string?)"doc_circuit_03_c0",
        LlmType = "self_correcting",
        MockAnswer = "The deterministic circuit breaker trips when consecutive grounding checks fail or when retrieved context exhibits severe deficit."
    },
    new
    {
        Name = "TC-03: Adversarial / OOD Query (Context Deficit)",
        Query = "What is the warp drive engine displacement of the Starship Enterprise?",
        ExpectedChunk = (string?)null,
        LlmType = "grounded",
        MockAnswer = "Starship Enterprise has warp factor 9."
    },
    new
    {
        Name = "TC-04: Persistent Hallucination Interception",
        Query = "How does ExecutionTracer monitor system telemetry?",
        ExpectedChunk = (string?)"doc_telemetry_04_c0",
        LlmType = "persistent_hallucinator",
        MockAnswer = "ExecutionTracer transmits logs to an unverified third-party cloud."
    }
};

Console.WriteLine("\n[2/3] Executing Quantitative Benchmark Matrix...\n");

int passedGates = 0;
float totalFaithfulness = 0;
long totalLatencyMs = 0;

for (int i = 0; i < benchmarkCases.Length; i++)
{
    var tc = benchmarkCases[i];
    ILlmClient llmClient = tc.LlmType switch
    {
        "self_correcting" => MockDeterministicLlmClient.CreateSelfCorrecting(
            "Initial hallucination: System uses quantum teleportation.",
            tc.MockAnswer),
        "persistent_hallucinator" => MockDeterministicLlmClient.CreatePersistentHallucinator(tc.MockAnswer),
        _ => MockDeterministicLlmClient.CreateGrounded(tc.MockAnswer)
    };

    var supervisor = new DeterministicSupervisor(pipeline, llmClient, groundingGuard, circuitBreaker);
    var request = new AgentExecutionRequest(tc.Query, MinimumRetrievalScore: 0.30f);

    var result = await supervisor.ExecuteAsync(request);
    var expectedChunks = tc.ExpectedChunk != null ? new[] { tc.ExpectedChunk } : null;
    var metrics = evaluator.EvaluateExecution(result, expectedSourceChunkIds: expectedChunks);

    totalFaithfulness += metrics.FaithfulnessScore;
    totalLatencyMs += metrics.LatencyMilliseconds;

    bool isExpectedOutcome = (tc.LlmType == "persistent_hallucinator" || tc.ExpectedChunk == null)
        ? result.IsFallback // Correctly intercepted and fallen back
        : metrics.PassedDeterministicGate;

    if (isExpectedOutcome) passedGates++;

    Console.ForegroundColor = ConsoleColor.White;
    Console.WriteLine($"--- [{tc.Name}] ---");
    Console.ResetColor();
    Console.WriteLine($" Query       : {tc.Query}");
    Console.WriteLine($" Final State : {result.FinalState} | IsFallback: {result.IsFallback} | Attempts: {result.RefinementAttempts}");
    Console.WriteLine($" Faithfulness: {metrics.FaithfulnessScore:P0} | Latency: {metrics.LatencyMilliseconds} ms | Recall: {metrics.ContextRecall:P0}");
    Console.WriteLine($" Final Output: {result.FinalOutput}");

    if (isExpectedOutcome)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(" Verdict     : [PASS - DETERMINISTIC GATE SATISFIED]\n");
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(" Verdict     : [FAIL - GATE VIOLATION]\n");
    }
    Console.ResetColor();
}

// 4. Summary Scorecard
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("===============================================================================");
Console.WriteLine("                         BENCHMARK SUMMARY SCORECARD");
Console.WriteLine("===============================================================================");
Console.ResetColor();

float successRate = (float)passedGates / benchmarkCases.Length;
Console.WriteLine($"Total Test Cases      : {benchmarkCases.Length}");
Console.WriteLine($"Deterministic Pass Rate: {successRate:P0} ({passedGates}/{benchmarkCases.Length}) [Test Set Scope]");
Console.WriteLine($"Avg Faithfulness Score : {(totalFaithfulness / benchmarkCases.Length):P1}");
Console.WriteLine($"Total Benchmark Time   : {totalLatencyMs} ms");
Console.WriteLine("Benchmark Gate Status : " + (successRate >= 1.0f ? "[PASSED: 100% of defined scenarios met deterministic criteria]" : "[FAILED]"));
Console.WriteLine("===============================================================================\n");
