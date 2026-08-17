namespace TrustLab.Domain.Models;

public sealed record Chunk(
    string Id,
    string DocumentId,
    string Content,
    int ChunkIndex,
    int StartOffset,
    int EndOffset,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static Chunk Create(
        string id,
        string documentId,
        string content,
        int chunkIndex,
        int startOffset,
        int endOffset,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(content);
        return new Chunk(id, documentId, content, chunkIndex, startOffset, endOffset, metadata ?? new Dictionary<string, string>());
    }
}

public sealed record RetrievalResult(
    Chunk Chunk,
    float Score,
    string RetrievalType,
    int? Rank = null);
