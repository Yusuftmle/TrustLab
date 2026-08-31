using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;
using TrustLab.Guardrails.Evaluation;
using TrustLab.Guardrails.Grounding;
using TrustLab.Infrastructure.Documents;
using TrustLab.Infrastructure.Embedding;
using TrustLab.Infrastructure.Llm;
using TrustLab.Infrastructure.Persistence;
using TrustLab.Rag.Chunking;
using TrustLab.Rag.Indexing;
using TrustLab.Rag.Reranking;
using Xunit;
using Xunit.Abstractions;

namespace TrustLab.UnitTests.Documents;

public class RunFourClinicalTests
{
    private readonly ITestOutputHelper _output;

    public RunFourClinicalTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task RunAndVerifyAllFourPdfCases()
    {
        string[] candidateDbPaths = [
            Path.Combine(AppContext.BaseDirectory, "data", "trustlab_corpus.db"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "TrustLab.Api", "bin", "Debug", "net10.0", "data", "trustlab_corpus.db")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "data", "trustlab_corpus.db"))
        ];

        string dbPath = candidateDbPaths.FirstOrDefault(File.Exists) ?? candidateDbPaths[0];
        var repo = new SqliteCorpusRepository(dbPath);
        await repo.InitializeAsync();
        var allChunks = await repo.GetAllChunksAsync();

        if (allChunks.Count == 0)
        {
            var loader = CompositeDocumentLoader.CreateDefault();
            var chunker = new SemanticBoundaryChunker();
            string folder = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "test_pdfs");
            if (!Directory.Exists(folder)) folder = @"c:\Users\hucks\OneDrive\Desktop\TrustLab\test_pdfs";

            foreach (var file in Directory.GetFiles(folder, "*.pdf"))
            {
                var fileName = Path.GetFileName(file);
                using var stream = File.OpenRead(file);
                var pages = await loader.LoadAsync(stream, fileName);
                var fileChunks = pages.SelectMany(p => chunker.ChunkDocument(p, 256, 32)).ToList();
                var fullText = string.Join("\n\n", pages.Select(p => p.Content));
                var doc = Document.Create(fileName, fullText);
                await repo.SaveDocumentWithChunksAsync(doc, fileChunks, new FileInfo(file).Length, pages.Count);
            }
            allChunks = await repo.GetAllChunksAsync();
        }

        Assert.True(allChunks.Count > 0, "Corpus chunks must be in SQLite.");

        var embedder = new DeterministicHashEmbedder(160);
        var sparseIndex = new Bm25SparseIndex();
        await sparseIndex.IndexChunksAsync(allChunks);

        var vectorStore = new DenseVectorStore();
        var contents = allChunks.Select(c => c.Content).ToList();
        var embeddings = await embedder.GenerateEmbeddingsAsync(contents);
        await vectorStore.UpsertAsync(allChunks.Zip(embeddings, (c, v) => (c, v)).ToList());

        var reranker = new CrossEncoderReranker();
        var llm = new OllamaLlmClient("http://localhost:11434", "qwen2.5:7b");
        var triadEvaluator = new RagTriadEvaluator();

        var testCases = new[]
        {
            new
            {
                Id = 1,
                DocExpected = "1751265070-en.pdf",
                Title = "Test 1: CABG & PNI Mortalite İlişkisi (Tuzak İddia Düzeltme)",
                Query = "PNI değeri sadece böbrek naklinde kullanılır, CABG cerrahisinde hiçbir etkisi yoktur."
            },
            new
            {
                Id = 2,
                DocExpected = "1751265119-en.pdf",
                Title = "Test 2: COVID-19 Enfeksiyonu & Koroner Arter Hastalığı",
                Query = "Türkiye'deki 1935 koroner anjiyografi hastasında COVID-19 ve aşıların koroner arter hastalığı üzerindeki etkisi nedir?"
            },
            new
            {
                Id = 3,
                DocExpected = "TKDA_53_4_238_246.pdf",
                Title = "Test 3: OPTIMUM Çalışması (DK-Culotte vs Mini-Culotte)",
                Query = "OPTIMUM çalışmasında bifurkasyon lezyonlarında Double Kissing Culotte ve Mini-Culotte tekniklerinin sonuçları nelerdir?"
            },
            new
            {
                Id = 4,
                DocExpected = "TKDA_53_5_304_311.pdf",
                Title = "Test 4: Malign Perikardiyal Efüzyon ve BT Attenüasyonu",
                Query = "Malign ve benign perikardiyal efüzyonun ayırıcı tanısında Bilgisayarlı Tomografi (BT) attenüasyon (HU) değerinin tanısal katkısı nedir?"
            }
        };

        foreach (var tc in testCases)
        {
            _output.WriteLine($"\n=======================================================");
            _output.WriteLine($"🩺 {tc.Title}");
            _output.WriteLine($"SORU / GİRDİ: \"{tc.Query}\"");

            // Retrieval
            var bm25Results = await sparseIndex.SearchAsync(tc.Query, topK: 15);
            var qVec = await embedder.GenerateEmbeddingAsync(tc.Query);
            var denseResults = await vectorStore.SearchAsync(qVec, topK: 15);

            var rrf = new TrustLab.Rag.Fusion.ReciprocalRankFusion(60);
            var fused = rrf.Fuse(new[] { (bm25Results, 1f), (denseResults, 1f) }, topK: 15);
            var reranked = await reranker.RerankAsync(tc.Query, fused, minimumRelevanceThreshold: 0f, topK: 5);

            var topChunks = reranked.Select(r => r.Chunk).ToList();

            // Prompt
            var ctx = new System.Text.StringBuilder();
            ctx.AppendLine("=== KLİNİK / BİLİMSEL BAĞLAM BELGELERİ ===");
            foreach (var (r, idx) in reranked.Select((r, i) => (r, i + 1)))
            {
                ctx.AppendLine($"[Kaynak {idx} | Belge: {r.Chunk.DocumentId}]");
                ctx.AppendLine(r.Chunk.Content);
                ctx.AppendLine();
            }
            ctx.AppendLine("==========================================");
            ctx.AppendLine($"KULLANICI / DOKTOR GİRDİSİ: {tc.Query}");
            ctx.AppendLine(@"GÖREV:
1. Yukarıdaki klinik bağlam belgelerindeki bilimsel bulguları kullanarak profesyonel bir Türkçe ile yanıtla.
2. Kullanıcının iddiası veya sorusu bağlam belgelerindeki bilgilerle çelişiyorsa ya da yanlış bir varsayım içeriyorsa (örneğin 'etkisizdir' veya 'başka alandadır' gibi uydurma iddialarda), belgedeki kesin verileri aktararak bu yanlış iddiayı nazikçe ve açıkça DÜZELT / ÇÜRÜT.
3. Yalnızca bağlam belgelerinde yazılı olan klinik verileri aktar.");

            var answer = await llm.GenerateResponseAsync(ctx.ToString(), "Sen bir klinik karar destek asistanısın. Yalnızca sağlanan bağlam belgelerine dayalı yanıtlar üretirsin.", 0.1f);
            var aVec = await embedder.GenerateEmbeddingAsync(answer);

            var eval = triadEvaluator.Evaluate(tc.Query, topChunks, answer, qVec, aVec);

            _output.WriteLine($"\n💡 ÜRETİLEN YANIT:\n{answer}");
            _output.WriteLine($"\n📊 4 ANA METRİK:");
            _output.WriteLine($"  1. Faithfulness (Olgusal Sadakat): %{(eval.Faithfulness * 100):F1}");
            _output.WriteLine($"  2. Hallucination Rate (Halüsinasyon Oranı): %{((1.0 - eval.Faithfulness) * 100):F1}");
            _output.WriteLine($"  3. Context Relevancy (Bağlam Hassasiyeti): %{(eval.ContextRelevancy * 100):F1}");
            _output.WriteLine($"  4. Answer Relevancy (Soru Uyumu): %{(eval.AnswerRelevancy * 100):F1}");

            _output.WriteLine("\n🔍 CÜMLE BAZLI DOĞRULAMA (SENTENCE GROUNDING):");
            foreach (var s in eval.SentenceDetails)
            {
                string status = s.IsGrounded ? "[✅ Olgusal Kanıt]" : "[❌ Desteksiz / Halüsinasyon]";
                _output.WriteLine($"  {status} Cümle #{s.SentenceIndex}: \"{s.Sentence}\"");
                if (s.IsGrounded && !string.IsNullOrWhiteSpace(s.BestMatchingSnippet))
                {
                    _output.WriteLine($"     ↳ Kaynak: {s.BestMatchingDocId} | Kanıt: \"{s.BestMatchingSnippet.Trim()}\"");
                }
            }
        }
    }
}
