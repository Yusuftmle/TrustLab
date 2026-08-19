using FluentAssertions;
using TrustLab.Domain.Models;
using TrustLab.Infrastructure.Embedding;
using TrustLab.Rag.Fusion;
using TrustLab.Rag.Indexing;
using TrustLab.Rag.Reranking;
using Xunit;

namespace TrustLab.UnitTests.Rag;

public class Bm25AndHybridTests
{
    [Fact]
    public async Task Bm25SparseIndex_ShouldRankExactMatchesHighest()
    {
        // Arrange
        var index = new Bm25SparseIndex();
        var chunks = new List<Chunk>
        {
            Chunk.Create("c1", "d1", "Deterministic guardrails prevent LLM hallucinations.", 0, 0, 50),
            Chunk.Create("c2", "d2", "Cooking Italian pasta requires boiling salted water.", 0, 0, 50),
            Chunk.Create("c3", "d3", "Zero hallucination guarantees are achieved via strict context grounding.", 0, 0, 70)
        };

        await index.IndexChunksAsync(chunks);

        // Act
        var results = await index.SearchAsync("hallucinations guardrails", topK: 2);

        // Assert
        results.Should().NotBeEmpty();
        results.First().Chunk.Id.Should().Be("c1");
        results.First().Score.Should().BeGreaterThan(0.0f);
    }

    [Fact]
    public async Task DenseVectorStore_ShouldPerformSimdCosineSimilarity()
    {
        // Arrange
        var store = new DenseVectorStore();
        var embedder = new DeterministicHashEmbedder(dimensions: 64);

        var chunk1 = Chunk.Create("c1", "d1", "Quantum computing algorithms for cryptography.", 0, 0, 45);
        var chunk2 = Chunk.Create("c2", "d2", "Baking chocolate chip cookies at home.", 0, 0, 38);

        var emb1 = await embedder.GenerateEmbeddingAsync(chunk1.Content);
        var emb2 = await embedder.GenerateEmbeddingAsync(chunk2.Content);

        await store.UpsertAsync(new[] { (chunk1, emb1), (chunk2, emb2) });

        // Act
        var queryVector = await embedder.GenerateEmbeddingAsync("quantum cryptographic algorithms");
        var results = await store.SearchAsync(queryVector, topK: 2);

        // Assert
        results.Should().HaveCount(2);
        results.First().Chunk.Id.Should().Be("c1");
        results.First().Score.Should().BeGreaterThan(results.Last().Score);
    }

    [Fact]
    public void ReciprocalRankFusion_ShouldMergeAndRankFairly()
    {
        // Arrange
        var rrf = new ReciprocalRankFusion(k: 60);
        var chunkA = Chunk.Create("A", "doc1", "Passage A", 0, 0, 10);
        var chunkB = Chunk.Create("B", "doc1", "Passage B", 0, 0, 10);
        var chunkC = Chunk.Create("C", "doc1", "Passage C", 0, 0, 10);

        var sparseList = new List<RetrievalResult>
        {
            new(chunkA, 2.5f, "Sparse", 1),
            new(chunkB, 1.2f, "Sparse", 2)
        };

        var denseList = new List<RetrievalResult>
        {
            new(chunkB, 0.95f, "Dense", 1),
            new(chunkC, 0.80f, "Dense", 2)
        };

        // Act (Chunk B appears in both lists, chunk A in one, chunk C in one)
        var fused = rrf.Fuse(new[] { (sparseList as IReadOnlyList<RetrievalResult>, 1.0f), (denseList as IReadOnlyList<RetrievalResult>, 1.0f) }, topK: 3);

        // Assert: Chunk B should win because it is rank 2 in sparse and rank 1 in dense (1/62 + 1/61 > 1/61)
        fused.Should().NotBeEmpty();
        fused.First().Chunk.Id.Should().Be("B");
        fused.Select(f => f.Chunk.Id).Should().Contain(new[] { "A", "B", "C" });
    }

    [Fact]
    public async Task CrossEncoderReranker_ShouldFilterOutIrrelevantNoise()
    {
        // Arrange
        var reranker = new CrossEncoderReranker();
        var relevantChunk = Chunk.Create("rel_1", "d1", "Strict Pydantic JSON schema enforcers eliminate parsing crashes.", 0, 0, 65);
        var noisyChunk = Chunk.Create("noise_1", "d2", "The weather in Seattle is rainy in November.", 0, 0, 45);

        var candidates = new List<RetrievalResult>
        {
            new(relevantChunk, 0.8f, "RRF", 1),
            new(noisyChunk, 0.1f, "RRF", 2)
        };

        // Act
        var filtered = await reranker.RerankAsync(
            query: "JSON schema enforcer crash elimination",
            candidates: candidates,
            minimumRelevanceThreshold: 0.30f,
            topK: 5);

        // Assert: Noise chunk should be rejected below cutoff
        filtered.Should().HaveCount(1);
        filtered.First().Chunk.Id.Should().Be("rel_1");
    }

    [Fact]
    public async Task OnnxGpuReranker_ShouldGracefullyFallbackAndRerank()
    {
        // Arrange (Testing OnnxGpuReranker with fallback to heuristic engine)
        using var gpuReranker = new OnnxGpuReranker(modelPath: "non_existent_model.onnx");
        var relevantChunk = Chunk.Create("rel_med", "doc_med", "Amoxicillin contraindications and severe penicillin allergy shock warnings.", 0, 0, 80);
        var noisyChunk = Chunk.Create("noise_culinary", "doc_food", "Best recipes for homemade sourdough bread baking.", 0, 0, 50);

        var candidates = new List<RetrievalResult>
        {
            new(relevantChunk, 0.9f, "RRF", 1),
            new(noisyChunk, 0.2f, "RRF", 2)
        };

        // Act
        var results = await gpuReranker.RerankAsync(
            query: "penicillin allergy amoxicillin contraindication",
            candidates: candidates,
            minimumRelevanceThreshold: 0.25f,
            topK: 5);

        // Assert
        results.Should().NotBeEmpty();
        results.First().Chunk.Id.Should().Be("rel_med");
        results.Should().NotContain(r => r.Chunk.Id == "noise_culinary");
    }
}

