using TrustLab.Domain.Models;

namespace TrustLab.Application.Interfaces;

/// <summary>
/// Farklı dosya formatlarından (.pdf, .txt, .md vb.) metin ve metadata çıkaran doküman yükleyici arayüzü.
/// Şifreli/kilitli PDF'ler için opsiyonel parola desteği sunar.
/// </summary>
public interface IDocumentLoader
{
    /// <summary>
    /// Bu loader verilen dosya uzantısını işleyebilir mi?
    /// </summary>
    bool CanHandle(string fileName);

    /// <summary>
    /// Dosya akışından doküman parçalarını (sayfalar veya bölümler) metin olarak çıkarır.
    /// </summary>
    /// <param name="stream">Dosya bayt akışı</param>
    /// <param name="fileName">Dosya adı (örn: prospektus.pdf)</param>
    /// <param name="password">Şifreli/kilitli PDF'ler için parola (opsiyonel)</param>
    /// <param name="cancellationToken">İptal belirteci</param>
    Task<IReadOnlyList<Document>> LoadAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default);
}
