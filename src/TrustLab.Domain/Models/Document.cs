namespace TrustLab.Domain.Models;

public sealed record Document(
    string Id,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static Document Create(string id, string content, IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(content);
        return new Document(id, content, metadata ?? new Dictionary<string, string>());
    }
}
