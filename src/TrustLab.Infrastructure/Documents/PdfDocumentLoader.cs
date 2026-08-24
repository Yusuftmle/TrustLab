using System.Text;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace TrustLab.Infrastructure.Documents;

/// <summary>
/// PDF dosyalarından sayfa bazlı metin ve metadata çıkaran yükleyici.
/// PdfPig kütüphanesi kullanarak şifreli/parolalı kilitli PDF'leri ve sayfa hiyerarşisini çözer.
/// </summary>
public sealed class PdfDocumentLoader : IDocumentLoader
{
    public bool CanHandle(string fileName)
    {
        return !string.IsNullOrWhiteSpace(fileName) &&
               fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<Document>> LoadAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var options = new ParsingOptions
        {
            Password = password,
            UseLenientParsing = true
        };

        var documents = new List<Document>();

        try
        {
            using var pdf = PdfDocument.Open(stream, options);

            for (int pageNum = 1; pageNum <= pdf.NumberOfPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = pdf.GetPage(pageNum);
                string pageText = ContentOrderTextExtractor.GetText(page);

                if (string.IsNullOrWhiteSpace(pageText))
                {
                    pageText = page.Text;
                }

                // Boş veya taranmış (resim) sayfaları tespit et
                bool isImageOnly = string.IsNullOrWhiteSpace(pageText) || pageText.Trim().Length < 10;
                string cleanContent = isImageOnly
                    ? $"[Uyarı: {fileName} Sayfa {pageNum} metin katmanı içermiyor (taranmış resim olabilir).]"
                    : pageText.Trim();

                var metadata = new Dictionary<string, string>
                {
                    ["SourceFile"] = fileName,
                    ["PageNumber"] = pageNum.ToString(),
                    ["TotalPages"] = pdf.NumberOfPages.ToString(),
                    ["FileType"] = "PDF",
                    ["IsScannedOrImageOnly"] = isImageOnly.ToString()
                };

                string docId = $"{fileName}#Sayfa_{pageNum}";
                documents.Add(Document.Create(docId, cleanContent, metadata));
            }

            return Task.FromResult<IReadOnlyList<Document>>(documents);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new InvalidOperationException(
                $"PDF belgesi ({fileName}) şifre ile kilitlenmiş. Lütfen 'Parola' alanına geçerli şifreyi giriniz. Detay: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"PDF belgesi ({fileName}) işlenirken hata oluştu: {ex.Message}", ex);
        }
    }
}
