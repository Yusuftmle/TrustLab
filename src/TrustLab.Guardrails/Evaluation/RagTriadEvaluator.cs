using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;
using TrustLab.Domain.Models;

namespace TrustLab.Guardrails.Evaluation;

public sealed record RagTriadScore(
    float ContextRelevancy,
    float Faithfulness,
    float AnswerRelevancy,
    IReadOnlyList<SentenceGroundingDetail> SentenceDetails);

public sealed record SentenceGroundingDetail(
    int SentenceIndex,
    string Sentence,
    bool IsGrounded,
    float SupportRatio,
    string? BestMatchingDocId,
    string? BestMatchingSnippet);

public sealed class RagTriadEvaluator
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "in", "with", "to", "for", "of", "as",
        "by", "that", "this", "it", "from", "be", "are", "was", "were", "been", "has", "have", "had",
        "bu", "bir", "ve", "ile", "için", "olan", "olarak", "veya", "ya", "da", "de", "ki", "ancak",
        "fakat", "ise", "gibi", "göre", "kadar", "çok", "daha", "en", "her", "tüm", "bazı", "mı", "mi",
        "mu", "mü", "var", "yok", "hakkında", "başka", "durum", "olduğu", "olduğunu", "yani", "tarafından",
        "belirten", "belirtmektedir", "belirtmemektedir", "göre"
    };

    private static bool IsConversationalOrMetaSentence(string sentence, IReadOnlyList<Chunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(sentence)) return true;
        
        // Cümle soru cümlesi mi veya çok kısa bir nezaket/bağlaç ifadesi mi?
        var tokens = Tokenizer.Tokenize(sentence).Where(t => !StopWords.Contains(t)).ToList();
        if (tokens.Count <= 2 && (sentence.EndsWith("?") || sentence.Length < 35))
        {
            return true;
        }

        return false;
    }

    public RagTriadScore Evaluate(
        string query,
        IReadOnlyList<Chunk> retrievedChunks,
        string generatedAnswer,
        ReadOnlyMemory<float>? queryVector = null,
        ReadOnlyMemory<float>? answerVector = null)
    {
        // 1. Context Relevancy: How much of retrieved context is relevant to query?
        float contextRelevancy = CalculateContextRelevancy(query, retrievedChunks);

        // 2. Faithfulness: How many claims in generated answer are backed by retrieved context?
        var (faithfulness, sentenceDetails) = CalculateFaithfulness(generatedAnswer, retrievedChunks);

        // 3. Answer Relevancy: Semantic alignment between query and generated answer
        float answerRelevancy = CalculateAnswerRelevancy(query, generatedAnswer, queryVector, answerVector);

        return new RagTriadScore(
            (float)Math.Round(contextRelevancy, 3),
            (float)Math.Round(faithfulness, 3),
            (float)Math.Round(answerRelevancy, 3),
            sentenceDetails);
    }

    private static float CalculateContextRelevancy(string query, IReadOnlyList<Chunk> chunks)
    {
        if (chunks.Count == 0 || string.IsNullOrWhiteSpace(query)) return 0f;

        var queryStems = Tokenizer.Tokenize(query)
            .Where(t => !StopWords.Contains(t))
            .Select(Tokenizer.Stem)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (queryStems.Count == 0) return 1f;

        int totalSentences = 0;
        int relevantSentences = 0;

        foreach (var chunk in chunks)
        {
            var chunkSentences = Regex.Split(chunk.Content, @"(?<=[.!?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            foreach (var cs in chunkSentences)
            {
                totalSentences++;
                var csTokens = Tokenizer.Tokenize(cs)
                    .Where(t => !StopWords.Contains(t))
                    .ToList();

                if (csTokens.Count > 0 && csTokens.Any(t => queryStems.Any(qs => Tokenizer.IsFuzzyStemMatch(t, qs))))
                {
                    relevantSentences++;
                }
            }
        }

        return totalSentences > 0 ? (float)relevantSentences / totalSentences : 0f;
    }

    private static (float Faithfulness, IReadOnlyList<SentenceGroundingDetail> Details) CalculateFaithfulness(
        string answer,
        IReadOnlyList<Chunk> chunks)
    {
        if (string.IsNullOrWhiteSpace(answer)) return (0f, Array.Empty<SentenceGroundingDetail>());
        if (chunks.Count == 0) return (0f, Array.Empty<SentenceGroundingDetail>());

        var sentences = Regex.Split(answer, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (sentences.Count == 0) return (1f, Array.Empty<SentenceGroundingDetail>());

        var allContextTokens = chunks
            .SelectMany(c => Tokenizer.Tokenize(c.Content))
            .Where(t => !StopWords.Contains(t))
            .ToList();

        var details = new List<SentenceGroundingDetail>();
        int groundedCount = 0;

        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];

            // 1. Selamlaşma, soru veya meta bildirimlerini dinamik tespit et
            if (IsConversationalOrMetaSentence(sentence, chunks))
            {
                groundedCount++;
                details.Add(new SentenceGroundingDetail(
                    SentenceIndex: i + 1,
                    Sentence: sentence,
                    IsGrounded: true,
                    SupportRatio: 1.0f,
                    BestMatchingDocId: "Klinik Diyalog / Meta Yanıt",
                    BestMatchingSnippet: "Klinik diyalog, yönlendirme veya bilgi yokluğu açıklaması."));
                continue;
            }

            var sTokens = Tokenizer.Tokenize(sentence)
                .Where(t => !StopWords.Contains(t))
                .ToList();

            if (sTokens.Count == 0)
            {
                groundedCount++;
                details.Add(new SentenceGroundingDetail(i + 1, sentence, true, 1.0f, null, null));
                continue;
            }

            // Türkçe morfolojik eşleşme
            int matched = sTokens.Count(st => allContextTokens.Any(ct => Tokenizer.IsFuzzyStemMatch(st, ct)));
            float supportRatio = (float)matched / sTokens.Count;
            
            // Factual eşik: en az %40 klinik kanıt örtüşmesi
            bool isGrounded = supportRatio >= 0.40f;

            if (isGrounded) groundedCount++;

            // En iyi eşleşen dokümanı ve o dokümandaki tam kanıt satırını bul
            string? bestDocId = null;
            string? bestSnippet = null;
            int maxDocMatches = 0;

            foreach (var chunk in chunks)
            {
                var chunkLines = chunk.Content.Split(new[] { '\n', '.', '•', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var cLine in chunkLines)
                {
                    if (cLine.Length < 10) continue;
                    var lineTokens = Tokenizer.Tokenize(cLine)
                        .Where(t => !StopWords.Contains(t))
                        .ToList();

                    int lineMatches = sTokens.Count(st => lineTokens.Any(lt => Tokenizer.IsFuzzyStemMatch(st, lt)));
                    if (lineMatches > maxDocMatches)
                    {
                        maxDocMatches = lineMatches;
                        bestDocId = chunk.DocumentId;
                        bestSnippet = cLine;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bestSnippet) && chunks.Count > 0)
            {
                bestSnippet = chunks[0].Content.Length > 120 ? chunks[0].Content[..120] + "..." : chunks[0].Content;
            }

            details.Add(new SentenceGroundingDetail(
                SentenceIndex: i + 1,
                Sentence: sentence,
                IsGrounded: isGrounded,
                SupportRatio: (float)Math.Round(supportRatio, 2),
                BestMatchingDocId: bestDocId ?? (chunks.Count > 0 ? chunks[0].DocumentId : "Kaynak Belge"),
                BestMatchingSnippet: bestSnippet ?? (chunks.Count > 0 ? chunks[0].Content : "")));
        }

        float faithfulness = (float)groundedCount / sentences.Count;
        return (faithfulness, details);
    }

    private static float CalculateAnswerRelevancy(string query, string answer, ReadOnlyMemory<float>? qVec, ReadOnlyMemory<float>? aVec)
    {
        if (qVec.HasValue && aVec.HasValue && qVec.Value.Length == aVec.Value.Length && qVec.Value.Length > 0)
        {
            var spanQ = qVec.Value.Span;
            var spanA = aVec.Value.Span;
            float dot = 0;
            for (int i = 0; i < spanQ.Length; i++) dot += spanQ[i] * spanA[i];
            return Math.Clamp(dot, 0f, 1f);
        }

        // Fallback lexical jaccard overlap if vectors not supplied
        var qTokens = Tokenizer.Tokenize(query).Where(t => !StopWords.Contains(t)).Select(Tokenizer.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aTokens = Tokenizer.Tokenize(answer).Where(t => !StopWords.Contains(t)).Select(Tokenizer.Stem).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (qTokens.Count == 0 || aTokens.Count == 0) return 0f;

        int intersection = qTokens.Count(t => aTokens.Contains(t));
        int union = qTokens.Union(aTokens, StringComparer.OrdinalIgnoreCase).Count();

        return union > 0 ? (float)intersection / union : 0f;
    }
}
