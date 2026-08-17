using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Rag.Chunking;

public sealed class SemanticBoundaryChunker : ITextChunker
{
    private static readonly char[] SentenceDelimiters = ['.', '!', '?', '\n'];

    public IReadOnlyList<Chunk> ChunkDocument(Document document, int maxTokensPerChunk = 256, int overlapTokens = 32)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(document.Content))
        {
            return Array.Empty<Chunk>();
        }

        if (overlapTokens >= maxTokensPerChunk)
        {
            throw new ArgumentException("Overlap tokens must be strictly less than maxTokensPerChunk.");
        }

        // Approximate token count (avg 4 chars per token)
        int maxChars = maxTokensPerChunk * 4;
        int overlapChars = overlapTokens * 4;

        var text = document.Content;
        var chunks = new List<Chunk>();
        int startOffset = 0;
        int chunkIndex = 0;

        while (startOffset < text.Length)
        {
            int targetEnd = Math.Min(startOffset + maxChars, text.Length);

            if (targetEnd < text.Length)
            {
                // Look for natural sentence or paragraph boundary near targetEnd
                int boundaryLookback = Math.Min(100, targetEnd - startOffset - 20);
                int bestBoundary = -1;

                for (int i = targetEnd; i >= targetEnd - boundaryLookback; i--)
                {
                    if (SentenceDelimiters.Contains(text[i]))
                    {
                        bestBoundary = i + 1;
                        break;
                    }
                }

                if (bestBoundary > startOffset)
                {
                    targetEnd = bestBoundary;
                }
                else
                {
                    // Fallback to space
                    int lastSpace = text.LastIndexOf(' ', targetEnd, Math.Min(50, targetEnd - startOffset));
                    if (lastSpace > startOffset)
                    {
                        targetEnd = lastSpace + 1;
                    }
                }
            }

            string chunkContent = text[startOffset..targetEnd].Trim();
            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                var metadata = new Dictionary<string, string>(document.Metadata ?? new Dictionary<string, string>())
                {
                    ["source_doc_id"] = document.Id,
                    ["chunk_index"] = chunkIndex.ToString()
                };

                chunks.Add(Chunk.Create(
                    id: $"{document.Id}_c{chunkIndex}",
                    documentId: document.Id,
                    content: chunkContent,
                    chunkIndex: chunkIndex,
                    startOffset: startOffset,
                    endOffset: targetEnd,
                    metadata: metadata));

                chunkIndex++;
            }

            if (targetEnd >= text.Length)
            {
                break;
            }

            // Slide window with overlap
            startOffset = Math.Max(targetEnd - overlapChars, startOffset + 1);
        }

        return chunks;
    }
}
