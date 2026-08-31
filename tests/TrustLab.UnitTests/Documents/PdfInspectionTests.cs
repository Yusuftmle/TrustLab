using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrustLab.Infrastructure.Documents;
using TrustLab.Rag.Chunking;
using Xunit;
using Xunit.Abstractions;

namespace TrustLab.UnitTests.Documents;

public class PdfInspectionTests
{
    private readonly ITestOutputHelper _output;

    public PdfInspectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task InspectAllDownloadedPdfs()
    {
        var loader = CompositeDocumentLoader.CreateDefault();
        var chunker = new SemanticBoundaryChunker();

        string folder = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test_pdfs");
        if (!Directory.Exists(folder))
        {
            folder = @"c:\Users\hucks\OneDrive\Desktop\TrustLab\test_pdfs";
        }

        var files = Directory.GetFiles(folder, "*.pdf");
        _output.WriteLine($"=== TOPLAM {files.Length} PDF BULUNDU ===");

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            _output.WriteLine($"\n==========================================");
            _output.WriteLine($"DOSYA: {fileName}");
            using var stream = File.OpenRead(file);
            var pages = await loader.LoadAsync(stream, fileName);
            _output.WriteLine($"Sayfa Sayısı: {pages.Count}");

            var fullText = string.Join("\n", pages.Select(p => p.Content));
            _output.WriteLine($"Toplam Karakter: {fullText.Length}");

            var chunks = pages.SelectMany(p => chunker.ChunkDocument(p, 256, 32)).ToList();
            _output.WriteLine($"Oluşan Chunk Sayısı: {chunks.Count}");

            _output.WriteLine("\n--- ÖZET / İLK 800 KARAKTER ---");
            _output.WriteLine(fullText.Length > 800 ? fullText[..800] : fullText);
        }
    }
}
