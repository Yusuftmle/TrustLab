using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Application.Interfaces;

public interface ITextChunker
{
    IReadOnlyList<Chunk> ChunkDocument(Document document, int maxTokensPerChunk = 256, int overlapTokens = 32);
}

public interface ITextEmbedder
{
    int Dimensions { get; }
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

public interface ISparseIndex
{
    Task IndexChunksAsync(IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(string query, int topK = 10, CancellationToken cancellationToken = default);
}

public interface IVectorStore
{
    Task UpsertAsync(IReadOnlyList<(Chunk Chunk, ReadOnlyMemory<float> Vector)> entries, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK = 10, CancellationToken cancellationToken = default);
}

public interface IReranker
{
    Task<IReadOnlyList<RetrievalResult>> RerankAsync(string query, IReadOnlyList<RetrievalResult> candidates, float minimumRelevanceThreshold = 0.3f, int topK = 5, CancellationToken cancellationToken = default);
}

public interface IHybridRetrievalPipeline
{
    Task IndexAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(string query, int topK = 5, float relevanceCutoff = 0.35f, CancellationToken cancellationToken = default);
}
