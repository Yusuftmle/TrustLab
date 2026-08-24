using System.Text;
using TrustLab.Infrastructure.Documents;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace TrustLab.UnitTests.Documents;

public sealed class DocumentLoaderTests
{
    [Fact]
    public async Task PlainTextDocumentLoader_ShouldLoadTxtAndMdFilesCorrectly()
    {
        // Arrange
        var loader = new PlainTextDocumentLoader();
        string sampleText = "Penisilin anafilaksi öyküsü olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir.";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sampleText));

        // Act
        var docs = await loader.LoadAsync(stream, "prospektus.md");

        // Assert
        Assert.Single(docs);
        Assert.Equal("prospektus.md", docs[0].Id);
        Assert.Equal(sampleText, docs[0].Content);
        Assert.Equal("MD", docs[0].Metadata?["FileType"]);
    }

    [Fact]
    public async Task PdfDocumentLoader_ShouldExtractTextFromPdf()
    {
        // Arrange
        var loader = new PdfDocumentLoader();
        
        // PdfPig ile hafızada basit bir test PDF'i oluşturalım
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842); // A4 boyutları
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        page.AddText("Parasetamol gunluk maksimum doz 4000mg'dir.", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        byte[] pdfBytes = builder.Build();

        using var stream = new MemoryStream(pdfBytes);

        // Act
        var docs = await loader.LoadAsync(stream, "parasetamol_prospektus.pdf");

        // Assert
        Assert.NotEmpty(docs);
        Assert.Contains("Parasetamol", docs[0].Content);
        Assert.Equal("1", docs[0].Metadata?["PageNumber"]);
        Assert.Equal("PDF", docs[0].Metadata?["FileType"]);
    }

    [Fact]
    public async Task CompositeDocumentLoader_ShouldRouteCorrectlyByExtension()
    {
        // Arrange
        var composite = CompositeDocumentLoader.CreateDefault();

        Assert.True(composite.CanHandle("test.pdf"));
        Assert.True(composite.CanHandle("data.md"));
        Assert.True(composite.CanHandle("notes.txt"));
        Assert.False(composite.CanHandle("image.png"));
    }
}
