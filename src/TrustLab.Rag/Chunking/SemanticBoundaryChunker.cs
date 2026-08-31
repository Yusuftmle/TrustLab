using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Rag.Chunking;

public sealed class SemanticBoundaryChunker : ITextChunker
{
    private static readonly char[] SentenceDelimiters = ['.', '!', '?', '\n', '•'];

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
            // Clean leading whitespace and leading dangling punctuation (like orphan hyphens, commas)
            while (startOffset < text.Length && (char.IsWhiteSpace(text[startOffset]) || text[startOffset] == '-' || text[startOffset] == ',' || text[startOffset] == ';'))
            {
                startOffset++;
            }

            if (startOffset >= text.Length)
            {
                break;
            }

            int targetEnd = Math.Min(startOffset + maxChars, text.Length);

            if (targetEnd < text.Length)
            {
                // Look for natural sentence or paragraph boundary near targetEnd
                int boundaryLookback = Math.Min(120, targetEnd - startOffset - 20);
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
                    // Fallback to word boundary (space)
                    int lastSpace = text.LastIndexOf(' ', targetEnd - 1, Math.Min(80, targetEnd - startOffset));
                    if (lastSpace > startOffset)
                    {
                        targetEnd = lastSpace + 1;
                    }
                }
            }

            string chunkContent = text[startOffset..targetEnd].Trim();
            // Remove any orphan leading/trailing punctuation left by edge cuts
            chunkContent = chunkContent.TrimStart('-', ',', ';', ':', ')').Trim();

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

            // =========================================================================
            // CRITICAL LESSON LEARNED: "The Naive Overlap Drift" Bug
            // -------------------------------------------------------------------------
            // ESKİ HATA:
            // startOffset = Math.Max(targetEnd - overlapChars, startOffset + 1);
            // 
            // Neden Patladı?
            // targetEnd cümle sonuna yaslansa bile, startOffset körü körüne 128 karakter
            // geriye çekildiğinde tam bir kelimenin ortasına basıyordu.
            // Örn: "Kullanmadan" -> "ullanmadan", "Stevens-Johnson" -> "s-Johnson".
            //
            // YENİ ÇÖZÜM: Bidirectional Snap-to-Boundary
            // 1. Önce overlap bölgesinde kalan en yakın CÜMLE BAŞLANGICINA kilitlen.
            // 2. Yoksa imleç kelime ortasındaysa bir sonraki TAM KELİME BAŞINA atla.
            // =========================================================================
            int nextStart = Math.Max(targetEnd - overlapChars, startOffset + 1);

            // Align nextStart to the start of a clean sentence or word boundary
            if (nextStart < text.Length)
            {
                // 1. First attempt: find sentence boundary in overlap region
                int sentenceBoundary = -1;
                for (int i = nextStart; i < targetEnd; i++)
                {
                    if (SentenceDelimiters.Contains(text[i]) && i + 1 < text.Length && (char.IsWhiteSpace(text[i + 1]) || text[i + 1] == '•'))
                    {
                        sentenceBoundary = i + 1;
                        break;
                    }
                }

                if (sentenceBoundary > startOffset && sentenceBoundary < targetEnd)
                {
                    nextStart = sentenceBoundary; // Cümle başına kilitlendi!
                }
                else
                {
                    // 2. Fallback: snap to the beginning of the whole word
                    // If in the middle of a word/hyphenated token, snap forward to the next whitespace
                    if (nextStart > 0 && !char.IsWhiteSpace(text[nextStart - 1]))
                    {
                        int nextSpace = text.IndexOf(' ', nextStart);
                        if (nextSpace > 0 && nextSpace < targetEnd)
                        {
                            nextStart = nextSpace + 1; // Asla yarım kelimeden başlama!
                        }
                    }
                }

                // Consume leading whitespace and orphan delimiters
                while (nextStart < text.Length && (char.IsWhiteSpace(text[nextStart]) || text[nextStart] == '-' || text[nextStart] == ',' || text[nextStart] == ';'))
                {
                    nextStart++;
                }
            }

            // Ensure forward progress to prevent infinite loop
            startOffset = Math.Max(nextStart, startOffset + 1);
        }

        return chunks;
    }
}
