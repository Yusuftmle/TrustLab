using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;

namespace TrustLab.Rag.Chunking;

/// <summary>
/// Deterministik Çift Yönlü Semantik Sınır Chunker (Bidirectional Snap-to-Boundary).
/// 
/// Güvenlik Kuralları:
/// 1. Ondalık Sayı / İstatistiki Veri Koruması (Numeric & Decimal Guard): Ondalık noktasından (P = 0.002) veya kısaltmalardan (vs., et al.) kesilmez.
/// 2. Kapanmamış Parantez Koruması (Unclosed Bracket Guard): [...] veya (...) içindeki noktalar cümle sonu sayılmaz.
/// 3. Çift Yönlü Snap: Hem bitişte hem örtüşme (overlap) başlangıcında asla yarım kelime veya yarım cümle üretmez.
/// </summary>
public sealed class SemanticBoundaryChunker : ITextChunker
{
    private static readonly char[] SentenceDelimiters = ['.', '!', '?', '\n', '•'];
    private static readonly string[] CommonAbbreviations = ["vs", "al", "eg", "ie", "dr", "prof", "fig", "tab", "ref", "no", "etc", "med", "vol"];

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
            // Clean leading whitespace and leading dangling punctuation
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
                int boundaryLookback = Math.Min(180, targetEnd - startOffset - 20);
                int bestBoundary = -1;

                // 1. Öncelik: Güvenli cümle/paragraf sonu ara
                for (int i = targetEnd; i >= targetEnd - boundaryLookback; i--)
                {
                    if (SentenceDelimiters.Contains(text[i]) && IsValidSentenceBoundary(text, i, startOffset))
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
                    // 2. Öncelik: Parantez dışındaki en yakın kelime boşluğu
                    int lastSpace = FindSafeWordBoundary(text, targetEnd, startOffset);
                    if (lastSpace > startOffset)
                    {
                        targetEnd = lastSpace + 1;
                    }
                }
            }

            string chunkContent = text[startOffset..targetEnd].Trim();
            chunkContent = chunkContent.TrimStart('-', ',', ';', ':', ')', ']').Trim();

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
            // Çift Yönlü Örtüşme (Bidirectional Overlap Snap)
            // =========================================================================
            int nextStart = Math.Max(targetEnd - overlapChars, startOffset + 1);

            if (nextStart < text.Length)
            {
                // 1. Overlap bölgesinde güvenli cümle başlangıcı ara
                int sentenceBoundary = -1;
                for (int i = nextStart; i < targetEnd; i++)
                {
                    if (SentenceDelimiters.Contains(text[i]) && IsValidSentenceBoundary(text, i, startOffset))
                    {
                        sentenceBoundary = i + 1;
                        break;
                    }
                }

                if (sentenceBoundary > startOffset && sentenceBoundary < targetEnd)
                {
                    nextStart = sentenceBoundary;
                }
                else
                {
                    // 2. Fallback: Tam kelime başına kilitlen (asla kelime ortasından başlama)
                    if (nextStart > 0 && !char.IsWhiteSpace(text[nextStart - 1]))
                    {
                        int nextSpace = text.IndexOf(' ', nextStart);
                        if (nextSpace > 0 && nextSpace < targetEnd)
                        {
                            nextStart = nextSpace + 1;
                        }
                    }
                }

                // Baştaki artık işaretleri ve boşlukları atla
                while (nextStart < text.Length && (char.IsWhiteSpace(text[nextStart]) || text[nextStart] == '-' || text[nextStart] == ',' || text[nextStart] == ';'))
                {
                    nextStart++;
                }
            }

            startOffset = Math.Max(nextStart, startOffset + 1);
        }

        return chunks;
    }

    /// <summary>
    /// Bir noktalama işaretinin gerçek bir cümle sonu olup olmadığını doğrular.
    /// Ondalık sayıları (0.002), kısaltmaları (vs., et al.) ve açık parantez içlerini korur.
    /// </summary>
    private static bool IsValidSentenceBoundary(string text, int index, int startOffset)
    {
        char c = text[index];

        if (c == '\n' || c == '•' || c == '!' || c == '?')
        {
            return true;
        }

        if (c == '.')
        {
            // 1. Ondalık sayı kontrolü: "0.002", "14.1%" vb.
            if (index + 1 < text.Length && char.IsDigit(text[index + 1]))
            {
                return false;
            }

            if (index > 0 && char.IsDigit(text[index - 1]) && index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1]) && text[index + 1] != ']' && text[index + 1] != ')')
            {
                return false;
            }

            // 2. Kısaltma kontrolü: "vs.", "al.", "Dr." vb.
            int wordStart = index - 1;
            while (wordStart >= startOffset && char.IsLetter(text[wordStart]))
            {
                wordStart--;
            }
            string prevWord = text[(wordStart + 1)..index].ToLowerInvariant();
            if (CommonAbbreviations.Contains(prevWord))
            {
                return false;
            }

            // 3. Parantez içi kontrolü: [ ... ] veya ( ... ) kapanmamışsa cümle bölme
            int bracketDepth = 0;
            int parenDepth = 0;
            for (int k = startOffset; k <= index; k++)
            {
                if (text[k] == '[') bracketDepth++;
                else if (text[k] == ']') bracketDepth = Math.Max(0, bracketDepth - 1);
                else if (text[k] == '(') parenDepth++;
                else if (text[k] == ')') parenDepth = Math.Max(0, parenDepth - 1);
            }

            if (bracketDepth > 0 || parenDepth > 0)
            {
                return false; // Parantez henüz kapanmadı!
            }

            // 4. Noktadan sonra en az bir boşluk veya kapanış parantezi gelmeli
            if (index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1]) && text[index + 1] != ']' && text[index + 1] != ')')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parantez bloklarını parçalamadan güvenli kelime boşluğu bulur.
    /// </summary>
    private static int FindSafeWordBoundary(string text, int targetEnd, int startOffset)
    {
        for (int i = targetEnd - 1; i >= Math.Max(startOffset, targetEnd - 100); i--)
        {
            if (text[i] == ' ')
            {
                // Parantez derinliğini kontrol et
                int bracketDepth = 0;
                for (int k = startOffset; k <= i; k++)
                {
                    if (text[k] == '[') bracketDepth++;
                    else if (text[k] == ']') bracketDepth = Math.Max(0, bracketDepth - 1);
                }

                if (bracketDepth == 0)
                {
                    return i;
                }
            }
        }

        return text.LastIndexOf(' ', targetEnd - 1, Math.Min(80, targetEnd - startOffset));
    }
}
