using TrustLab.Domain.Models;

namespace TrustLab.Application.Interfaces;

public record SavedDocumentInfo(
    string Id,
    string FileName,
    long FileSizeBytes,
    int TotalPages,
    int TotalCharacters,
    int TotalChunks,
    string UploadedAt,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface ICorpusRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveDocumentWithChunksAsync(Document document, IReadOnlyList<Chunk> chunks, long fileSizeBytes, int totalPages, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SavedDocumentInfo>> GetAllDocumentSummariesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Document>> GetAllDocumentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Chunk>> GetAllChunksAsync(CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentByIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Chunk>> GetChunksByDocumentIdAsync(string documentId, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken = default);
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
