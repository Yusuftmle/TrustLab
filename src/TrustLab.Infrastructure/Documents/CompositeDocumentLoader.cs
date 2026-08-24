using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Infrastructure.Documents;

/// <summary>
/// Tüm kayıtlı doküman yükleyicilerini (.pdf, .txt, .md vb.) yöneten birleşik yükleyici.
/// Dosya uzantısına göre en uygun yükleyiciyi otomatik seçer.
/// </summary>
public sealed class CompositeDocumentLoader : IDocumentLoader
{
    private readonly IEnumerable<IDocumentLoader> _loaders;

    public CompositeDocumentLoader(IEnumerable<IDocumentLoader> loaders)
    {
        _loaders = loaders ?? throw new ArgumentNullException(nameof(loaders));
    }

    public static CompositeDocumentLoader CreateDefault()
    {
        return new CompositeDocumentLoader(new IDocumentLoader[]
        {
            new PdfDocumentLoader(),
            new PlainTextDocumentLoader()
        });
    }

    public bool CanHandle(string fileName)
    {
        return _loaders.Any(l => l.CanHandle(fileName));
    }

    public async Task<IReadOnlyList<Document>> LoadAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var loader = _loaders.FirstOrDefault(l => l.CanHandle(fileName));

        if (loader == null)
        {
            string ext = Path.GetExtension(fileName);
            throw new NotSupportedException(
                $"Desteklenmeyen dosya formatı ({ext}). Desteklenen formatlar: .pdf, .txt, .md, .json, .csv");
        }

        return await loader.LoadAsync(stream, fileName, password, cancellationToken);
    }
}
