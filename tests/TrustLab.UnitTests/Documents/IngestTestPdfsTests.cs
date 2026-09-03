using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrustLab.Domain.Models;
using TrustLab.Infrastructure.Documents;
using TrustLab.Infrastructure.Persistence;
using TrustLab.Rag.Chunking;
using Xunit;

namespace TrustLab.UnitTests.Documents;

public class IngestTestPdfsTests
{
    [Fact]
    public async Task IngestAllFourPdfsToSqlite()
    {
        string[] targetDbPaths = [
            Path.Combine(AppContext.BaseDirectory, "data", "trustlab_corpus.db"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "TrustLab.Api", "bin", "Debug", "net10.0", "data", "trustlab_corpus.db")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data", "trustlab_corpus.db"))
        ];

        var loader = CompositeDocumentLoader.CreateDefault();
        var chunker = new SemanticBoundaryChunker();

        string folder = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test_pdfs");
        if (!Directory.Exists(folder)) folder = @"c:\Users\hucks\OneDrive\Desktop\TrustLab\test_pdfs";

        var files = Directory.GetFiles(folder, "*.pdf");
        Assert.Equal(4, files.Length);

        foreach (var dbPath in targetDbPaths)
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var repo = new SqliteCorpusRepository(dbPath);
            await repo.InitializeAsync();
            await repo.ClearAllAsync();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                using var stream = File.OpenRead(file);
                var pages = await loader.LoadAsync(stream, fileName);

                var fileChunks = new System.Collections.Generic.List<Chunk>();
                foreach (var p in pages)
                {
                    fileChunks.AddRange(chunker.ChunkDocument(p, 256, 32));
                }

                var fullText = string.Join("\n\n", pages.Select(p => p.Content));
                var page1Meta = pages.Count > 0 ? pages[0].Metadata : null;
                var combinedDoc = Document.Create(fileName, fullText, page1Meta);

                await repo.SaveDocumentWithChunksAsync(combinedDoc, fileChunks, new FileInfo(file).Length, pages.Count);
            }

            var summaries = await repo.GetAllDocumentSummariesAsync();
            Assert.Equal(4, summaries.Count);
        }
    }
}
