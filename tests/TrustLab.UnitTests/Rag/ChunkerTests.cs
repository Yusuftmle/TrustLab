using FluentAssertions;
using TrustLab.Domain.Models;
using TrustLab.Rag.Chunking;
using Xunit;

namespace TrustLab.UnitTests.Rag;

public class ChunkerTests
{
    [Fact]
    public void ChunkDocument_ShouldSplitOnSentenceBoundaries_AndPreserveMetadata()
    {
        // Arrange
        var chunker = new SemanticBoundaryChunker();
        var document = Document.Create(
            id: "doc_001",
            content: "Deterministic guardrails are critical for LLM reliability. They eliminate hallucinations in production. Clean Architecture ensures maintainability.",
            metadata: new Dictionary<string, string> { ["category"] = "ai_reliability" });

        // Act
        var chunks = chunker.ChunkDocument(document, maxTokensPerChunk: 15, overlapTokens: 2);

        // Assert
        chunks.Should().NotBeEmpty();
        chunks.All(c => c.DocumentId == "doc_001").Should().BeTrue();
        chunks.All(c => c.Metadata != null && c.Metadata["category"] == "ai_reliability").Should().BeTrue();
        chunks.First().Content.Should().Contain("Deterministic guardrails");
    }

    [Fact]
    public void ChunkDocument_EmptyContent_ShouldReturnEmptyList()
    {
        var chunker = new SemanticBoundaryChunker();
        var document = Document.Create("doc_empty", "");

        var chunks = chunker.ChunkDocument(document);

        chunks.Should().BeEmpty();
    }
}
