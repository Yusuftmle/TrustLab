using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrustLab.Domain.Models;
using TrustLab.Infrastructure.Documents;
using TrustLab.Rag.Chunking;
using Xunit;

namespace TrustLab.UnitTests.Documents;

/// <summary>
/// Layout-Aware PDF Yükleyici, Drop-Cap, Bölüm Etiketleri ve İstatistiki Blok Koruması Testleri.
/// </summary>
public class LayoutAwarePdfLoaderTests
{
    private readonly string _testPdfsDir;

    public LayoutAwarePdfLoaderTests()
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test_pdfs");
        if (!Directory.Exists(dir))
        {
            dir = @"c:\Users\hucks\OneDrive\Desktop\TrustLab\test_pdfs";
        }
        _testPdfsDir = dir;
    }

    [Fact]
    public async Task PdfDocumentLoader_ExtractsPage1BibliographicMetadata_AndSeparatesFromChunkBody()
    {
        var loader = new PdfDocumentLoader();
        string pdfPath = Path.Combine(_testPdfsDir, "TKDA_53_4_238_246.pdf");
        Assert.True(File.Exists(pdfPath), $"Test PDF dosyası bulunamadı: {pdfPath}");

        using var stream = File.OpenRead(pdfPath);
        var pages = await loader.LoadAsync(stream, "TKDA_53_4_238_246.pdf");

        Assert.NotEmpty(pages);
        var page1 = pages[0];

        Assert.True(page1.Metadata.ContainsKey("Doi"), "DOI metadata alanı eksik.");
        Assert.Contains("10.5543/tkda.2025.98697", page1.Metadata["Doi"]);

        Assert.True(page1.Metadata.ContainsKey("Citation"), "Citation metadata alanı eksik.");
        Assert.Contains("Kahraman S", page1.Metadata["Citation"]);

        Assert.True(page1.Metadata.ContainsKey("Journal"), "Journal metadata alanı eksik.");
        Assert.Contains("Turk Kardiyol", page1.Metadata["Journal"]);

        Assert.StartsWith("ABSTRACT", page1.Content.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("238 ARCHIVES OF THE TURKISH SOCIETY", page1.Content);
    }

    [Fact]
    public async Task PdfDocumentLoader_StripsRunningHeaders_FromIntermediatePages()
    {
        var loader = new PdfDocumentLoader();
        string pdfPath = Path.Combine(_testPdfsDir, "TKDA_53_4_238_246.pdf");
        using var stream = File.OpenRead(pdfPath);

        var pages = await loader.LoadAsync(stream, "TKDA_53_4_238_246.pdf");

        Assert.True(pages.Count >= 3);
        var page2 = pages[1];
        var page3 = pages[2];

        Assert.False(page2.Content.Trim().StartsWith("239 Turk Kardiyol Dern Ars", StringComparison.OrdinalIgnoreCase));
        Assert.False(page3.Content.Trim().StartsWith("240 Turk Kardiyol Dern Ars", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Chunker_NeverSplits_InsideStatisticalBracketBlocks_OrOnDecimalPoints()
    {
        var chunker = new SemanticBoundaryChunker();

        // 1000 karakterlik klinik istatistik cümlesi
        string clinicalText = "The primary endpoint was target lesion failure (TLF), defined as a composite of cardiac death, " +
                              "target vessel myocardial infarction (TVMI), and target lesion revascularization (TLR) at one- and three-year follow up. " +
                              "Results: The incidence of TLF was significantly lower in the DKC group at both one year [7 (7.6%) vs. 1 (0.9%), P = 0.017] " +
                              "and three years [18 (19.6%) vs. 6 (5.6%), P = 0.002], primarily driven by a reduction in TLR at one year [6 (6.5%) vs. 1 (0.9%), P = 0.033] " +
                              "and three years [13 (14.1%) vs. 5 (4.6%), P = 0.018]. Fewer patients experienced TVMI [4 (4.3%) vs. 3 (2.8%), P = 0.551].";

        var doc = Document.Create("clinical_test.txt", clinicalText);
        var chunks = chunker.ChunkDocument(doc, maxTokensPerChunk: 100, overlapTokens: 16);

        // Doğrulama: Hiçbir chunk "P = 0." veya "vs." gibi yarım sayı/kısaltma ile bitmemeli
        foreach (var chunk in chunks)
        {
            Assert.False(chunk.Content.EndsWith("P = 0."), "Chunk ondalık noktasından (P = 0.) bölünmemeli!");
            Assert.False(chunk.Content.EndsWith("vs."), "Chunk 'vs.' kısaltmasından bölünmemeli!");
            Assert.False(chunk.Content.StartsWith("002]"), "Chunk bölünmüş ondalık artığıyla (002]) başlamamalı!");
        }
    }

    [Fact]
    public async Task Chunking_TKDA_Pdf_ProducesZeroMidCitationCutoffs_AndEnrichedMetadata()
    {
        var loader = new PdfDocumentLoader();
        var chunker = new SemanticBoundaryChunker();
        string pdfPath = Path.Combine(_testPdfsDir, "TKDA_53_4_238_246.pdf");
        using var stream = File.OpenRead(pdfPath);

        var pages = await loader.LoadAsync(stream, "TKDA_53_4_238_246.pdf");
        var allChunks = pages.SelectMany(p => chunker.ChunkDocument(p, 256, 32)).ToList();

        Assert.NotEmpty(allChunks);

        var chunk1 = allChunks[0];
        Assert.StartsWith("ABSTRACT", chunk1.Content.Trim(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cite this article as:", chunk1.Content);
        Assert.False(chunk1.Content.EndsWith("P = 0."), "Chunk 1 ondalık noktasında (P = 0.) bölünmemeli.");

        // Drop-cap ve Bölüm Başlığı Temizliği Doğrulaması
        var chunk4 = allChunks[3];
        Assert.DoesNotContain("ORIGINAL ARTICLE", chunk4.Content);
        Assert.DoesNotContain("KLİNİK ÇALIŞMA", chunk4.Content);
        Assert.DoesNotContain("C oronary", chunk4.Content); // Drop-Cap "Coronary" olmalı
        Assert.Contains("Coronary bifurcation lesion", chunk4.Content);

        foreach (var chunk in allChunks.Take(5))
        {
            Assert.True(chunk.Metadata.ContainsKey("Doi"), "Chunk DOI metadata taşımalı.");
            Assert.True(chunk.Metadata.ContainsKey("Citation"), "Chunk Citation metadata taşımalı.");
            Assert.Equal("10.5543/tkda.2025.98697", chunk.Metadata["Doi"]);
        }
    }
}
