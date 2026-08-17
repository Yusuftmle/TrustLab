using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;
using TrustLab.Rag.Fusion;

namespace TrustLab.Rag.Pipeline;

public sealed class HybridRetrievalPipeline : IHybridRetrievalPipeline
{
    private readonly ITextChunker _chunker;
    private readonly ISparseIndex _sparseIndex;
    private readonly IVectorStore _vectorStore;
    private readonly ITextEmbedder _embedder;
    private readonly IReranker _reranker;
    private readonly ReciprocalRankFusion _rrf;

    public HybridRetrievalPipeline(
        ITextChunker chunker,
        ISparseIndex sparseIndex,
        IVectorStore vectorStore,
        ITextEmbedder embedder,
        IReranker reranker,
        int rrfConstant = 60)
    {
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _sparseIndex = sparseIndex ?? throw new ArgumentNullException(nameof(sparseIndex));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _reranker = reranker ?? throw new ArgumentNullException(nameof(reranker));
        _rrf = new ReciprocalRankFusion(rrfConstant);
    }

    public async Task IndexAsync(IReadOnlyList<Document> documents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var allChunks = new List<Chunk>();
        foreach (var doc in documents)
        {
            var chunks = _chunker.ChunkDocument(doc);
            allChunks.AddRange(chunks);
        }

        if (allChunks.Count == 0)
        {
            return;
        }

        // 1. Index in BM25 Sparse Engine
        await _sparseIndex.IndexChunksAsync(allChunks, cancellationToken);

        // 2. Generate Dense Embeddings & Upsert to Vector Store
        var contents = allChunks.Select(c => c.Content).ToList();
        var embeddings = await _embedder.GenerateEmbeddingsAsync(contents, cancellationToken);

        var denseEntries = allChunks
            .Zip(embeddings, (chunk, vector) => (chunk, vector))
            .ToList();

        await _vectorStore.UpsertAsync(denseEntries, cancellationToken);
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        string query,
        int topK = 5,
        float relevanceCutoff = 0.35f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<RetrievalResult>();
        }

        // 1. Run Sparse (BM25) Search
        var sparseResults = await _sparseIndex.SearchAsync(query, topK: topK * 2, cancellationToken);

        // 2. Run Dense Vector Search
        var queryVector = await _embedder.GenerateEmbeddingAsync(query, cancellationToken);
        var denseResults = await _vectorStore.SearchAsync(queryVector, topK: topK * 2, cancellationToken);

        // 3. Reciprocal Rank Fusion (RRF)
        var rankedLists = new List<(IReadOnlyList<RetrievalResult> Results, float Weight)>
        {
            (sparseResults, 1.0f),
            (denseResults, 1.0f)
        };

        var fusedResults = _rrf.Fuse(rankedLists, topK: topK * 2);

        // 4. Cross-Encoder Re-Ranking & Noise Rejection Filter
        var reranked = await _reranker.RerankAsync(
            query,
            fusedResults,
            minimumRelevanceThreshold: relevanceCutoff,
            topK: topK,
            cancellationToken: cancellationToken);

        return reranked;
    }
}
