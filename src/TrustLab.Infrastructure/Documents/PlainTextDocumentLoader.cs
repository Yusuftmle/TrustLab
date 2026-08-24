using System.Text;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Infrastructure.Documents;

/// <summary>
/// Düz metin, Markdown, JSON ve CSV formatlarındaki belgeleri UTF-8 olarak okur.
/// </summary>
public sealed class PlainTextDocumentLoader : IDocumentLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".csv", ".log"
    };

    public bool CanHandle(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        string ext = Path.GetExtension(fileName);
        return SupportedExtensions.Contains(ext);
    }

    public async Task<IReadOnlyList<Document>> LoadAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        string content = await reader.ReadToEndAsync(cancellationToken);

        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        var metadata = new Dictionary<string, string>
        {
            ["SourceFile"] = fileName,
            ["FileType"] = ext.TrimStart('.').ToUpperInvariant(),
            ["CharacterCount"] = content.Length.ToString()
        };

        var doc = Document.Create(fileName, content.Trim(), metadata);
        return new List<Document> { doc };
    }
}
