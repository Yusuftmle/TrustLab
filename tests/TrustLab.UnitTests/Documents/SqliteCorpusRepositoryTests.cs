using FluentAssertions;
using TrustLab.Domain.Models;
using TrustLab.Infrastructure.Persistence;
using Xunit;

namespace TrustLab.UnitTests.Documents;

public class SqliteCorpusRepositoryTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteCorpusRepository _repository;

    public SqliteCorpusRepositoryTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"trustlab_test_{Guid.NewGuid():N}.db");
        _repository = new SqliteCorpusRepository(_testDbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try { File.Delete(_testDbPath); } catch { }
        }
    }

    [Fact]
    public async Task SaveDocumentWithChunks_AndRetrieve_ShouldWorkPersistently()
    {
        // Arrange
        await _repository.InitializeAsync();
        var doc = Document.Create("PAROL-500.pdf", "Parol hafif ve orta şiddetli ağrılarda kullanılır.", new Dictionary<string, string> { ["source"] = "titck" });
        var chunk1 = Chunk.Create("PAROL-500.pdf_c0", "PAROL-500.pdf", "Parol hafif ve orta", 0, 0, 19);
        var chunk2 = Chunk.Create("PAROL-500.pdf_c1", "PAROL-500.pdf", "şiddetli ağrılarda kullanılır.", 1, 20, 50);

        // Act
        await _repository.SaveDocumentWithChunksAsync(doc, new[] { chunk1, chunk2 }, fileSizeBytes: 1024, totalPages: 1);

        // Assert
        var summaries = await _repository.GetAllDocumentSummariesAsync();
        summaries.Should().HaveCount(1);
        summaries[0].FileName.Should().Be("PAROL-500.pdf");
        summaries[0].TotalChunks.Should().Be(2);

        var retrievedDoc = await _repository.GetDocumentByIdAsync("PAROL-500.pdf");
        retrievedDoc.Should().NotBeNull();
        retrievedDoc!.Content.Should().Contain("Parol hafif ve orta");

        var retrievedChunks = await _repository.GetChunksByDocumentIdAsync("PAROL-500.pdf");
        retrievedChunks.Should().HaveCount(2);

        // Test delete
        var deleted = await _repository.DeleteDocumentAsync("PAROL-500.pdf");
        deleted.Should().BeTrue();

        var summariesAfterDelete = await _repository.GetAllDocumentSummariesAsync();
        summariesAfterDelete.Should().BeEmpty();
    }
}
