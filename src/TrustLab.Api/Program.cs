using System.Diagnostics;
using Microsoft.Extensions.FileProviders;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Common;
using TrustLab.Domain.Models;
using TrustLab.Guardrails.CircuitBreaker;
using TrustLab.Guardrails.Evaluation;
using TrustLab.Guardrails.Grounding;
using TrustLab.Guardrails.Schema;
using TrustLab.Infrastructure.Documents;
using TrustLab.Infrastructure.Embedding;
using TrustLab.Infrastructure.Llm;
using TrustLab.Rag.Chunking;
using TrustLab.Rag.Fusion;
using TrustLab.Rag.Indexing;
using TrustLab.Rag.Pipeline;
using TrustLab.Rag.Reranking;
using TrustLab.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Config kısayolları — tüm magic değerler appsettings.json'da
var cfg = builder.Configuration.GetSection("TrustLab");
var rerankerCfg   = cfg.GetSection("Reranker");
var embedderCfg   = cfg.GetSection("Embedder");
var pipelineCfg   = cfg.GetSection("Pipeline");
var cbCfg         = cfg.GetSection("CircuitBreaker");
var stressCfg     = cfg.GetSection("StressTest");

string gpuDeviceName  = rerankerCfg["GpuDeviceName"]  ?? "GPU (DirectML)";
string cpuFallback    = rerankerCfg["CpuFallbackName"] ?? "CrossEncoder CPU";
string modelFileName  = rerankerCfg["ModelFileName"]   ?? "ms-marco-MiniLM-L-6-v2.onnx";
string vocabFileName  = rerankerCfg["VocabFileName"]   ?? "vocab.txt";
int    deviceId       = rerankerCfg.GetValue<int>("DeviceId", 0);
int    embedDims      = embedderCfg.GetValue<int>("Dimensions", 128);
int    cbMaxFails     = cbCfg.GetValue<int>("MaxConsecutiveFailures", 2);
string chatCorpus     = cfg["ChatCorpusDefault"] ?? string.Empty;

// Pipeline defaults (UI slider defaults ile eşleşmeli)
int   defMaxTokens   = pipelineCfg.GetValue<int>("DefaultMaxTokensPerChunk", 256);
int   defOverlap     = pipelineCfg.GetValue<int>("DefaultOverlapTokens", 32);
int   defRrfK        = pipelineCfg.GetValue<int>("DefaultRrfK", 60);
float defThreshold   = pipelineCfg.GetValue<float>("DefaultRerankThreshold", 0.25f);
float defMinScore    = pipelineCfg.GetValue<float>("DefaultMinimumRetrievalScore", 0.20f);
int   defTopK        = pipelineCfg.GetValue<int>("DefaultTopK", 3);

// Stress test sabit değerler
int    stressEmbedDims  = stressCfg.GetValue<int>("EmbedderDimensions", 128);
string stressGpuQuery   = stressCfg["GpuNeedleQuery"] ?? "penicillin allergy amoxicillin contraindication";
float  stressGpuThresh  = stressCfg.GetValue<float>("GpuRerankThreshold", 0.20f);
int    stressGpuTopK    = stressCfg.GetValue<int>("GpuTopK", 3);

// LLM (Ollama) config
var llmCfg          = cfg.GetSection("Llm").GetSection("Ollama");
string ollamaBaseUrl = llmCfg["BaseUrl"]    ?? "http://localhost:11434";
string ollamaModel   = llmCfg["Model"]      ?? "qwen2.5:7b";
float  ollamaTemp    = llmCfg.GetValue<float>("Temperature", 0.1f);
bool   autoPull      = llmCfg.GetValue<bool>("AutoPullModel", true);
string systemPrompt  = llmCfg["SystemPrompt"] ??
    "Sen bir klinik karar destek asistanısın. Yalnızca sağlanan bağlam belgelerine dayalı yanıtlar üretirsin.";

// 1. Dependency Injection Services
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddSingleton<ITextChunker, SemanticBoundaryChunker>();
builder.Services.AddSingleton<ISparseIndex, Bm25SparseIndex>();
builder.Services.AddSingleton<IVectorStore, DenseVectorStore>();
builder.Services.AddSingleton<ITextEmbedder>(_ => new DeterministicHashEmbedder(dimensions: embedDims));
builder.Services.AddSingleton<IReranker>(sp =>
{
    string[] candidateDirs = [
        AppContext.BaseDirectory,
        Directory.GetCurrentDirectory(),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
        Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."))
    ];

    string modelPath = "";
    string vocabPath = "";

    foreach (var dir in candidateDirs)
    {
        var m = Path.Combine(dir, "models", modelFileName);
        var v = Path.Combine(dir, "models", vocabFileName);
        if (File.Exists(m) && string.IsNullOrEmpty(modelPath)) modelPath = m;
        if (File.Exists(v) && string.IsNullOrEmpty(vocabPath)) vocabPath = v;
    }

    return new OnnxGpuReranker(modelPath, vocabPath, deviceId: deviceId, fallbackReranker: new CrossEncoderReranker());
});

builder.Services.AddSingleton<IGroundingGuard, NgramGroundingGuard>();
builder.Services.AddSingleton<ISchemaValidator, JsonSchemaEnforcer>();
builder.Services.AddSingleton<RagTriadEvaluator>();
builder.Services.AddSingleton<ExecutionTracer>();
builder.Services.AddTransient<IHybridRetrievalPipeline, HybridRetrievalPipeline>();

// LLM istemcisi: Ollama yerel sunucu
builder.Services.AddSingleton<ILlmClient>(_ => new OllamaLlmClient(ollamaBaseUrl, ollamaModel));

// Çok formatlı Doküman Yükleyici (PDF, TXT, MD, JSON, CSV)
builder.Services.AddSingleton<IDocumentLoader>(_ => CompositeDocumentLoader.CreateDefault());

var app = builder.Build();

app.UseCors();

// 2. Serve UI Static Files
var uiPath = Path.Combine(app.Environment.ContentRootPath, "..", "..", "ui");
if (!Directory.Exists(uiPath))
{
    uiPath = Path.Combine(Directory.GetCurrentDirectory(), "ui");
}

if (Directory.Exists(uiPath))
{
    var fileProvider = new PhysicalFileProvider(Path.GetFullPath(uiPath));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider, RequestPath = "" });
}
else
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// 3. API Endpoints

// GET /api/system/status
app.MapGet("/api/system/status", (IReranker reranker) =>
{
    var onnxReranker = reranker as OnnxGpuReranker;
    bool gpuAvailable = onnxReranker?.IsGpuAvailable ?? false;
    return Results.Ok(new
    {
        Status = "Online",
        Framework = $".NET {System.Environment.Version.Major}.0",
        SimdHardwareAcceleration = System.Numerics.Vector.IsHardwareAccelerated,
        SimdVectorByteSize = System.Numerics.Vector<float>.Count * 4,
        GpuDirectMlAvailable = gpuAvailable,
        GpuDevice = gpuAvailable ? gpuDeviceName : cpuFallback,
        OnnxModelLoaded = onnxReranker?.IsModelLoaded ?? false,
        MemoryAllocatedMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2)
    });
});

// POST /api/documents/upload — PDF (şifreli/kilitli dahil), TXT, MD, JSON, CSV Yükleme & Çıkarım
app.MapPost("/api/documents/upload", async (
    HttpRequest request,
    [Microsoft.AspNetCore.Mvc.FromServices] IDocumentLoader documentLoader,
    [Microsoft.AspNetCore.Mvc.FromServices] ITextChunker chunker) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0)
    {
        return Results.BadRequest(new { Error = "Lütfen yüklenecek bir dosya seçiniz (.pdf, .txt, .md, .json, .csv)." });
    }

    var file = request.Form.Files[0];
    string? password = request.Form.TryGetValue("password", out var pw) && !string.IsNullOrWhiteSpace(pw) ? pw.ToString() : null;

    if (file.Length == 0)
    {
        return Results.BadRequest(new { Error = "Yüklenen dosya boş." });
    }

    try
    {
        using var stream = file.OpenReadStream();
        var documents = await documentLoader.LoadAsync(stream, file.FileName, password);

        var allChunks = new List<Chunk>();
        foreach (var doc in documents)
        {
            allChunks.AddRange(chunker.ChunkDocument(doc, 256, 32));
        }

        var totalChars = documents.Sum(d => d.Content.Length);

        return Results.Ok(new
        {
            FileName = file.FileName,
            FileSizeBytes = file.Length,
            TotalPagesOrDocs = documents.Count,
            TotalChunks = allChunks.Count,
            TotalCharacters = totalChars,
            CombinedText = string.Join("\n\n", documents.Select(d => d.Content)),
            Documents = documents.Select(d => new
            {
                d.Id,
                Preview = d.Content.Length > 250 ? d.Content[..250] + "..." : d.Content,
                Length = d.Content.Length,
                d.Metadata
            }),
            Chunks = allChunks.Select(c => new
            {
                c.Id,
                c.DocumentId,
                c.Content,
                c.ChunkIndex,
                Length = c.Content.Length
            })
        });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
}).DisableAntiforgery();

// POST /api/lab/evaluate — Endüstri Standardı RAG Triad, Sıralama Metrikleri & Donanım Profiling
app.MapPost("/api/lab/evaluate", async (
    [Microsoft.AspNetCore.Mvc.FromBody] LabEvaluationRequest req,
    [Microsoft.AspNetCore.Mvc.FromServices] ITextChunker chunker,
    [Microsoft.AspNetCore.Mvc.FromServices] ITextEmbedder embedder,
    [Microsoft.AspNetCore.Mvc.FromServices] IReranker reranker,
    [Microsoft.AspNetCore.Mvc.FromServices] RagTriadEvaluator triadEvaluator) =>
{
    var totalTimer = Stopwatch.StartNew();

    if (string.IsNullOrWhiteSpace(req.Query) || string.IsNullOrWhiteSpace(req.Corpus))
    {
        return Results.BadRequest(new { Error = "Query and Corpus cannot be empty." });
    }

    // 1. Ingestion & Chunking
    var ingestTimer = Stopwatch.StartNew();
    var allChunks = new List<Chunk>();
    var docs = new List<Document>();
    int maxTokens = req.MaxTokensPerChunk > 0 ? req.MaxTokensPerChunk : 256;
    int overlapTokens = req.OverlapTokens >= 0 && req.OverlapTokens < maxTokens ? req.OverlapTokens : 32;

    if (req.Corpus.Contains("Doküman 1:") || req.Corpus.Contains("Doküman 2:"))
    {
        var lines = req.Corpus.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        docs = lines.Select((line, idx) => Document.Create($"doc_{idx + 1}", line)).ToList();
        foreach (var doc in docs)
        {
            allChunks.AddRange(chunker.ChunkDocument(doc, maxTokens, overlapTokens));
        }
    }
    else
    {
        var mainDoc = Document.Create("uploaded_doc", req.Corpus);
        docs.Add(mainDoc);
        allChunks.AddRange(chunker.ChunkDocument(mainDoc, maxTokens, overlapTokens));
    }
    ingestTimer.Stop();

    if (allChunks.Count == 0)
    {
        return Results.Ok(new { Error = "No chunks created." });
    }

    // 2. BM25 Sparse Index
    var sparseTimer = Stopwatch.StartNew();
    var sparseIndex = new Bm25SparseIndex();
    await sparseIndex.IndexChunksAsync(allChunks);
    var sparseResults = await sparseIndex.SearchAsync(req.Query, topK: allChunks.Count);
    sparseTimer.Stop();

    // 3. SIMD Dense Vector Index
    var denseTimer = Stopwatch.StartNew();
    var vectorStore = new DenseVectorStore();
    var contents = allChunks.Select(c => c.Content).ToList();
    var embeddings = await embedder.GenerateEmbeddingsAsync(contents);
    var denseEntries = allChunks.Zip(embeddings, (chunk, vector) => (chunk, vector)).ToList();
    await vectorStore.UpsertAsync(denseEntries);

    var queryVector = await embedder.GenerateEmbeddingAsync(req.Query);
    var denseResults = await vectorStore.SearchAsync(queryVector, topK: allChunks.Count);
    denseTimer.Stop();

    // 4. Reciprocal Rank Fusion
    int rrfK = req.RrfK > 0 ? req.RrfK : defRrfK;
    var rrf = new ReciprocalRankFusion(rrfK);
    var rankedLists = new List<(IReadOnlyList<RetrievalResult> Results, float Weight)>
    {
        (sparseResults, 1.0f),
        (denseResults, 1.0f)
    };
    var fusedResults = rrf.Fuse(rankedLists, topK: allChunks.Count);

    // 5. Cross-Encoder / DirectML GPU Re-Ranking
    var gpuTimer = Stopwatch.StartNew();
    float threshold = req.RerankThreshold > 0 ? req.RerankThreshold : defThreshold;
    var rerankedResults = await reranker.RerankAsync(
        req.Query,
        fusedResults,
        minimumRelevanceThreshold: threshold,
        topK: allChunks.Count);
    gpuTimer.Stop();

    // 6. RAG Triad Evaluation
    var triadTimer = Stopwatch.StartNew();
    var retrievedTopChunks = rerankedResults.Select(r => r.Chunk).ToList();
    ReadOnlyMemory<float>? answerVector = !string.IsNullOrWhiteSpace(req.CandidateResponse)
        ? await embedder.GenerateEmbeddingAsync(req.CandidateResponse)
        : null;

    var triadScore = triadEvaluator.Evaluate(
        req.Query,
        retrievedTopChunks,
        req.CandidateResponse ?? string.Empty,
        queryVector,
        answerVector);
    triadTimer.Stop();

    // 7. Ranking Metrics (Precision@K, Recall@K, MRR, NDCG@K)
    // Otomatik olarak sorudaki anahtar kelimeleri en çok içeren dokümanı relevant kabul et
    var queryStems = Tokenizer.Tokenize(req.Query)
        .Where(t => t.Length > 2)
        .Select(Tokenizer.Stem)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var relevantDocIds = docs
        .Where(d => Tokenizer.Tokenize(d.Content).Select(Tokenizer.Stem).Any(s => queryStems.Contains(s)))
        .Select(d => d.Id)
        .ToHashSet();

    if (relevantDocIds.Count == 0 && docs.Count > 0) relevantDocIds.Add(docs[0].Id);

    var retrievedDocIds = rerankedResults.Select(r => r.Chunk.DocumentId).Distinct().ToList();
    var rankingMetrics = RankingMetricsCalculator.Calculate(retrievedDocIds, relevantDocIds, k: 3);

    totalTimer.Stop();

    return Results.Ok(new
    {
        Query = req.Query,
        CandidateResponse = req.CandidateResponse,
        TotalDocuments = docs.Count,
        TotalChunks = allChunks.Count,
        
        // RAG Triad Scores
        RagTriad = new
        {
            ContextRelevancy = triadScore.ContextRelevancy,
            Faithfulness = triadScore.Faithfulness,
            AnswerRelevancy = triadScore.AnswerRelevancy,
            SentenceDetails = triadScore.SentenceDetails
        },

        // Ranking Metrics
        RankingMetrics = new
        {
            PrecisionAtK = rankingMetrics.PrecisionAtK,
            RecallAtK = rankingMetrics.RecallAtK,
            Mrr = rankingMetrics.Mrr,
            NdcgAtK = rankingMetrics.NdcgAtK,
            K = rankingMetrics.K
        },

        // Hardware Timers (Microsecond Precision)
        HardwareProfiling = new
        {
            IngestMs = Math.Round(ingestTimer.Elapsed.TotalMilliseconds, 3),
            Bm25SearchMs = Math.Round(sparseTimer.Elapsed.TotalMilliseconds, 3),
            SimdDenseSearchMs = Math.Round(denseTimer.Elapsed.TotalMilliseconds, 3),
            GpuRerankMs = Math.Round(gpuTimer.Elapsed.TotalMilliseconds, 3),
            TriadEvalMs = Math.Round(triadTimer.Elapsed.TotalMilliseconds, 3),
            TotalLatencyMs = Math.Round(totalTimer.Elapsed.TotalMilliseconds, 3),
            MemoryAllocatedMb = Math.Round(GC.GetTotalMemory(false) / (1024.0 * 1024.0), 2),
            GpuDevice = reranker is OnnxGpuReranker g && g.IsGpuAvailable ? gpuDeviceName : cpuFallback
        },

        // Retrieval Results
        SparseResults = sparseResults.Select(r => new { r.Chunk.Id, r.Chunk.DocumentId, r.Chunk.Content, Score = Math.Round(r.Score, 4), r.Rank }),
        DenseResults = denseResults.Select(r => new { r.Chunk.Id, r.Chunk.DocumentId, r.Chunk.Content, Score = Math.Round(r.Score, 4), r.Rank }),
        FusedResults = fusedResults.Select(r => new { r.Chunk.Id, r.Chunk.DocumentId, r.Chunk.Content, Score = Math.Round(r.Score, 5), r.Rank }),
        RerankedResults = rerankedResults.Select(r => new { r.Chunk.Id, r.Chunk.DocumentId, r.Chunk.Content, Score = Math.Round(r.Score, 4), r.Rank, r.RetrievalType }),

        // Geometric Vector Coordinates
        QueryVector = queryVector,
        DocVectors = denseEntries.Select(e => new
        {
            e.chunk.Id,
            e.chunk.DocumentId,
            e.chunk.Content,
            Vector = e.vector,
            CosineDistance = Math.Round(1.0 - CosineSim(queryVector, e.vector), 4),
            EuclideanDistance = Math.Round(EuclideanDist(queryVector, e.vector), 4)
        })
    });
});

// POST /api/lab/stress-test — Needle in a Haystack Dahil 4 Bilimsel Stres Testi
app.MapPost("/api/lab/stress-test", async (
    [Microsoft.AspNetCore.Mvc.FromServices] IReranker gpuReranker) =>
{
    var chunker = new SemanticBoundaryChunker();
    var sparseIndex = new Bm25SparseIndex();
    var vectorStore = new DenseVectorStore();
    var embedder = new DeterministicHashEmbedder(dimensions: stressEmbedDims);
    var reranker = new CrossEncoderReranker();
    var pipeline = new HybridRetrievalPipeline(chunker, sparseIndex, vectorStore, embedder, reranker);
    var groundingGuard = new NgramGroundingGuard();
    var circuitBreaker = new DeterministicCircuitBreaker(maxConsecutiveFailures: cbMaxFails);
    var evaluator = new TrustLab.Agents.Evaluation.DeterministicEvaluationService();

    // 10 Alakasız Doküman + 1 Tıbbi Kritik İğne ("Needle in a Haystack")
    var corpus = new List<Document>
    {
        Document.Create("noise_01", "İtalyan mutfağında spagetti yaparken tenceredeki su kaynadıktan sonra tuz atılmalıdır."),
        Document.Create("noise_02", "Brezilya kahve çekirdekleri 200 derecede 15 dakika kavrulduğunda orta sertlikte aroma verir."),
        Document.Create("noise_03", "Antarktika penguenleri dondurucu rüzgarlara karşı toplu halde birbirlerine sokularak ısınırlar."),
        Document.Create("needle_med_04", "KRİTİK TIBBİ PROTOKOL: Şiddetli penisilin anafilaksi öyküsü olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir, alternatif makrolid başlanmalıdır."),
        Document.Create("noise_05", "Mars keşif aracı Perseverance Jezero kraterinde antik nehir deltası tortularını araştırmaktadır."),
        Document.Create("noise_06", "C# 13 ile gelen params Collections özelliği dizi tahsisi yapmadan generic koleksiyon almayı sağlar."),
        Document.Create("noise_07", "Dizel motorlarda buji bulunmaz, yakıt sıkışan havanın yüksek sıcaklığıyla kendiliğinden tutuşur."),
        Document.Create("noise_08", "Satrançta Sicilya Savunması 1.e4 c5 hamleleriyle başlar ve siyahlar için agresif bir karşı saldırı sunar."),
        Document.Create("noise_09", "Amazon yağmur ormanları dünyadaki biyolojik çeşitliliğin yüzde onundan fazlasına ev sahipliği yapar."),
        Document.Create("noise_10", "Optik fiber kablolar veriyi tam iç yansıma prensibiyle ışık hızına yakın hızlarda iletir.")
    };

    var ingestSw = Stopwatch.StartNew();
    await pipeline.IndexAsync(corpus);
    ingestSw.Stop();

    var cases = new[]
    {
        new
        {
            Id = "TC-01",
            Name = "Direct Context Retrieval (Factual Precision@1)",
            Query = "Penisilin alerjisi olan hastada amoksisilin kontrendikasyonu nedir?",
            ExpectedChunk = (string?)"needle_med_04_c0",
            LlmType = "grounded",
            MockAnswer = "Şiddetli penisilin anafilaksi öyküsü olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir, alternatif makrolid başlanmalıdır."
        },
        new
        {
            Id = "TC-02",
            Name = "Self-Correction / Refusal Test (Sıfır Halüsinasyon)",
            Query = "Brezilya kahvesinin çekirdek kavurma süresi nedir?",
            ExpectedChunk = (string?)"noise_02_c0",
            LlmType = "self_correcting",
            MockAnswer = "Brezilya kahve çekirdekleri 200 derecede 15 dakika kavrulduğunda orta sertlikte aroma verir."
        },
        new
        {
            Id = "TC-03",
            Name = "Context Deficit / Out-of-Domain (Tuzak Soru)",
            Query = "Kuantum bilgisayarlarında Shor algoritması RSA şifrelerini nasıl kırar?",
            ExpectedChunk = (string?)null,
            LlmType = "grounded",
            MockAnswer = "Kuantum bilgisayarlar Shor algoritması ile 2048-bit RSA şifrelerini saniyeler içinde çözer."
        },
        new
        {
            Id = "TC-04",
            Name = "Noise Resistance: Needle in a Haystack (10 Çöp Arasından İğneyi Çekme)",
            Query = "Şiddetli penisilin alerjisinde hangi antibiyotik alternatiftir?",
            ExpectedChunk = (string?)"needle_med_04_c0",
            LlmType = "grounded",
            MockAnswer = "Penisilin anafilaksisinde alternatif olarak makrolid grubu antibiyotik başlanmalıdır."
        }
    };

    var results = new List<object>();
    int passedCount = 0;
    float totalFaithfulness = 0;
    long totalLatency = 0;

    foreach (var tc in cases)
    {
        ILlmClient llmClient = tc.LlmType switch
        {
            "self_correcting" => MockDeterministicLlmClient.CreateSelfCorrecting(
                "Uydurma: Kahve 500 derecede 2 dakikada kavrulur.",
                tc.MockAnswer),
            _ => MockDeterministicLlmClient.CreateGrounded(tc.MockAnswer)
        };

        var supervisor = new TrustLab.Agents.Supervisor.DeterministicSupervisor(pipeline, llmClient, groundingGuard, circuitBreaker);
        var req = new TrustLab.Application.Interfaces.AgentExecutionRequest(tc.Query, MinimumRetrievalScore: defMinScore);
        
        var sw = Stopwatch.StartNew();
        var execResult = await supervisor.ExecuteAsync(req);
        sw.Stop();

        var expectedChunks = tc.ExpectedChunk != null ? new[] { tc.ExpectedChunk } : null;
        var metrics = evaluator.EvaluateExecution(execResult, expectedSourceChunkIds: expectedChunks);

        totalFaithfulness += metrics.FaithfulnessScore;
        totalLatency += sw.ElapsedMilliseconds;

        bool isExpected = tc.ExpectedChunk == null ? execResult.IsFallback : metrics.PassedDeterministicGate;
        if (isExpected) passedCount++;

        results.Add(new
        {
            tc.Id,
            tc.Name,
            tc.Query,
            FinalState = execResult.FinalState.ToString(),
            IsFallback = execResult.IsFallback,
            RefinementAttempts = execResult.RefinementAttempts,
            Faithfulness = Math.Round(metrics.FaithfulnessScore, 2),
            LatencyMs = sw.ElapsedMilliseconds,
            Recall = Math.Round(metrics.ContextRecall, 2),
            FinalOutput = execResult.FinalOutput,
            Passed = isExpected,
            Verdict = isExpected ? "✅ PASS (Sıfır Halüsinasyon)" : "❌ FAIL (Kural İhlali)"
        });
    }

    // Run Live GPU Check
    double gpuMs = 0;
    float gpuTopScore = 0;
    string gpuTopChunk = "";
    if (gpuReranker is OnnxGpuReranker onnxGpu && onnxGpu.IsModelLoaded)
    {
        var gpuTimer = Stopwatch.StartNew();
        var candidates = new List<RetrievalResult>
        {
            new(Chunk.Create("c1", "noise_01", "İtalyan mutfağında spagetti yaparken su kaynamalıdır.", 0, 0, 50), 0.30f, "RRF", 1),
            new(Chunk.Create("c2", "needle_med_04", "Şiddetli penisilin anafilaksi öyküsünde Amoksisilin mutlak kontrendikedir.", 0, 0, 80), 0.75f, "RRF", 2),
            new(Chunk.Create("c3", "noise_05", "Mars keşif aracı krater araştırmaktadır.", 0, 0, 40), 0.20f, "RRF", 3)
        };
        var gpuReranked = await onnxGpu.RerankAsync(stressGpuQuery, candidates, stressGpuThresh, stressGpuTopK);
        gpuTimer.Stop();
        gpuMs = Math.Round(gpuTimer.Elapsed.TotalMilliseconds, 2);
        gpuTopScore = gpuReranked.Count > 0 ? (float)Math.Round(gpuReranked[0].Score, 4) : 0;
        gpuTopChunk = gpuReranked.Count > 0 ? gpuReranked[0].Chunk.Content : "";
    }

    return Results.Ok(new
    {
        TotalTests = cases.Length,
        PassedTests = passedCount,
        PassRate = Math.Round((double)passedCount / cases.Length * 100, 1),
        AvgFaithfulness = Math.Round((totalFaithfulness / cases.Length) * 100, 1),
        TotalLatencyMs = totalLatency,
        IngestTimeMs = ingestSw.ElapsedMilliseconds,
        GpuBenchmark = new
        {
            IsGpuActive = gpuReranker is OnnxGpuReranker g && g.IsGpuAvailable,
            Device = gpuReranker is OnnxGpuReranker g2 && g2.IsGpuAvailable ? gpuDeviceName : cpuFallback,
            LatencyMs = gpuMs,
            TopScore = gpuTopScore,
            TopChunk = gpuTopChunk
        },
        TestCases = results
    });
});

// POST /api/chat/rag — Canlı Sağlık Chat & RAG Observability Endpoint'i (Gerçek Ollama LLM)
app.MapPost("/api/chat/rag", async (
    [Microsoft.AspNetCore.Mvc.FromBody] ChatRagRequest req,
    [Microsoft.AspNetCore.Mvc.FromServices] ITextChunker chunker,
    [Microsoft.AspNetCore.Mvc.FromServices] ITextEmbedder embedder,
    [Microsoft.AspNetCore.Mvc.FromServices] IReranker reranker,
    [Microsoft.AspNetCore.Mvc.FromServices] ILlmClient llmClient,
    [Microsoft.AspNetCore.Mvc.FromServices] RagTriadEvaluator triadEvaluator) =>
{
    var totalTimer = Stopwatch.StartNew();

    if (string.IsNullOrWhiteSpace(req.Query))
    {
        return Results.BadRequest(new { Error = "Query cannot be empty." });
    }

    string corpus = !string.IsNullOrWhiteSpace(req.Corpus) ? req.Corpus : chatCorpus;

    // 1. Ingest & Chunking
    var ingestTimer = Stopwatch.StartNew();
    var allChunks = new List<Chunk>();
    string docName = !string.IsNullOrWhiteSpace(req.DocumentName) ? req.DocumentName : "Dokuman.pdf";

    if (corpus.Contains("Doküman 1:") || corpus.Contains("Doküman 2:"))
    {
        var lines = corpus.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var docs = lines.Select((line, idx) => Document.Create($"Bolum_{idx + 1}.pdf", line)).ToList();
        foreach (var doc in docs)
        {
            allChunks.AddRange(chunker.ChunkDocument(doc, 256, 32));
        }
    }
    else
    {
        var mainDoc = Document.Create(docName, corpus);
        allChunks.AddRange(chunker.ChunkDocument(mainDoc, 256, 64));
    }

    if (allChunks.Count == 0)
    {
        allChunks.Add(new Chunk("c_0", docName, corpus, 0, 0, Tokenizer.Tokenize(corpus).Count));
    }
    ingestTimer.Stop();

    // 2. BM25 Search
    var bm25Timer = Stopwatch.StartNew();
    var sparseIndex = new Bm25SparseIndex();
    await sparseIndex.IndexChunksAsync(allChunks);
    var sparseResults = await sparseIndex.SearchAsync(req.Query, topK: allChunks.Count);
    bm25Timer.Stop();

    // 3. SIMD Dense Vector Search
    var denseTimer = Stopwatch.StartNew();
    var vectorStore = new DenseVectorStore();
    var contents = allChunks.Select(c => c.Content).ToList();
    var embeddings = await embedder.GenerateEmbeddingsAsync(contents);
    var denseEntries = allChunks.Zip(embeddings, (chunk, vector) => (chunk, vector)).ToList();
    await vectorStore.UpsertAsync(denseEntries);

    var queryVector = await embedder.GenerateEmbeddingAsync(req.Query);
    var denseResults = await vectorStore.SearchAsync(queryVector, topK: allChunks.Count);
    denseTimer.Stop();

    // 4. RRF Fusion
    var rrf = new ReciprocalRankFusion(60);
    var rankedLists = new List<(IReadOnlyList<RetrievalResult> Results, float Weight)>
    {
        (sparseResults, 1.0f),
        (denseResults, 1.0f)
    };
    var fusedResults = rrf.Fuse(rankedLists, topK: allChunks.Count);

    // 5. Cross-Encoder / DirectML GPU Re-Ranking
    var gpuTimer = Stopwatch.StartNew();
    var rerankedResults = await reranker.RerankAsync(
        req.Query,
        fusedResults,
        minimumRelevanceThreshold: 0.0f,
        topK: 5);
    gpuTimer.Stop();

    // Vektör Uzayı & Re-ranker Güven Skoru (GPU Cross-Encoder Gerçek Anlamsal Eşik: 0.15)
    float maxGpuScore = rerankedResults.Count > 0 ? rerankedResults.Max(r => r.Score) : 0f;
    float maxBm25Score = sparseResults.Count > 0 ? sparseResults.Max(r => r.Score) : 0f;
    bool isDocRelevant = (maxGpuScore >= 0.15f || maxBm25Score >= 1.5f);

    // 6. RAG Prompt oluştur + Ollama LLM'den gerçek yanıt al
    var llmTimer = Stopwatch.StartNew();
    string finalAnswer;

    if (!string.IsNullOrWhiteSpace(req.CandidateResponseOverride))
    {
        // Test modu: override yanıtı kullan (grounding testi için)
        finalAnswer = req.CandidateResponseOverride;
    }
    else if (isDocRelevant && rerankedResults.Count > 0)
    {
        // Gerçek Klinik Doküman RAG Modu: Soru dokümanla anlamsal olarak örtüştü
        var contextBuilder = new System.Text.StringBuilder();
        contextBuilder.AppendLine("=== KLİNİK BAĞLAM BELGELERİ ===");
        foreach (var (result, idx) in rerankedResults.Where(r => r.Score > 0.05f).Select((r, i) => (r, i + 1)))
        {
            contextBuilder.AppendLine($"[Kaynak {idx} | Sayfa/Bölüm: {result.Chunk.DocumentId}]");
            contextBuilder.AppendLine(result.Chunk.Content);
            contextBuilder.AppendLine();
        }
        contextBuilder.AppendLine("==============================");
        contextBuilder.AppendLine($"DOKTOR / HASTA SORUSU: {req.Query}");
        contextBuilder.AppendLine(@"GÖREV VE KLİNİK KURALLAR:
1. Yukarıdaki klinik bağlam belgelerindeki doğrulanmış bilgileri kullanarak soruyu profesyonel bir Türkçe ile yanıtla.
2. Kullanıcının sorduğu miktar belgedeki güvenli maksimum dozu aşıyorsa, belgedeki yasal limiti belirterek aşırı doz tehlikesine karşı uyar.
3. Yalnızca bağlam belgelerinde yazılı olan doz ve bilgileri aktar, belgede geçmeyen hiçbir dozajı veya yan etkiyi uydurma.");

        finalAnswer = await llmClient.GenerateResponseAsync(
            prompt: contextBuilder.ToString(),
            systemInstruction: systemPrompt,
            temperature: ollamaTemp);
    }
    else
    {
        // Genel Sohbet / Selamlama Modu: Vektör uzayında klinik soru eşleşmesi yok
        string activeDocText = !string.IsNullOrWhiteSpace(req.DocumentName)
            ? $"Yüklü aktif belge: '{req.DocumentName}'."
            : "Yüklenen klinik belgeler";

        var chatPrompt = $"Kullanıcı mesajı: \"{req.Query}\"\n{activeDocText}\n" +
                         "Bu mesaja Türkçe, tek veya iki cümlelik çok kısa, nazik ve profesyonel bir selamlama yanıtı ver. Kullanıcıya yüklenen belge veya sağlık konusunda ne sormak istediğini sor.";

        finalAnswer = await llmClient.GenerateResponseAsync(
            prompt: chatPrompt,
            systemInstruction: "Sen Türkçe konuşan profesyonel bir klinik karar destek yapay zeka asistanısın. Selamlama, hal hatır sorma ve genel sohbet mesajlarına kısa, nazik ve yardımsever bir dille yanıt ver.",
            temperature: 0.2f);
    }

    if (string.IsNullOrWhiteSpace(finalAnswer))
    {
        finalAnswer = "Ollama LLM bağlantı hatası — lütfen Ollama servisinin çalıştığını doğrulayın.";
    }
    llmTimer.Stop();

    // 7. Sentence-by-Sentence Grounding & Trace Extraction
    var topChunks = rerankedResults.Select(r => r.Chunk).ToList();
    ReadOnlyMemory<float>? answerVector = await embedder.GenerateEmbeddingAsync(finalAnswer);

    // Vektörel Karar: Eğer sorgu klinik dokümanla eşleşmiyorsa (isDocRelevant == false), yanıt doğrudan Klinik Diyalogdur
    var triadScore = !isDocRelevant 
        ? new RagTriadScore(
            0f,
            1.0f,
            1.0f,
            finalAnswer.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((s, i) => new SentenceGroundingDetail(i + 1, s, true, 1.0f, "Klinik Diyalog / Sohbet", "Klinik diyalog ve genel selamlama yanıtı.")).ToList())
        : triadEvaluator.Evaluate(req.Query, topChunks, finalAnswer, queryVector, answerVector);

    // 8. Dosage & Numerical Guardrail
    var dosageCheck = isDocRelevant ? DosageAndNumericGuard.VerifyDosages(finalAnswer, topChunks) : new DosageVerificationResult(true, Array.Empty<string>(), Array.Empty<string>(), "Sohbet mesajı: Dozaj denetimi atlandı.");

    totalTimer.Stop();

    float topConfidence = rerankedResults.Count > 0 ? (float)Math.Round(rerankedResults[0].Score, 2) : 0f;
    float rerankLift = (rerankedResults.Count > 0 && denseResults.Count > 0)
        ? (float)Math.Round((rerankedResults[0].Score - denseResults[0].Score) * 100, 1)
        : 0f;

    string guardrailStatus = (!isDocRelevant || (triadScore.Faithfulness >= 0.70f && dosageCheck.IsValid))
        ? "PASSED / VERIFIED"
        : "BLOCKED / WARNING";

    return Results.Ok(new
    {
        MessageId = $"msg_{Guid.NewGuid().ToString("N")[..8]}",
        Query = req.Query,
        Answer = finalAnswer,
        
        // Inline Tracing Data for Sentences
        Sentences = triadScore.SentenceDetails.Select(s => new
        {
            s.SentenceIndex,
            s.Sentence,
            s.IsGrounded,
            SupportRatio = s.SupportRatio,
            BestDoc = s.BestMatchingDocId ?? "Bilinmiyor",
            Snippet = s.BestMatchingSnippet ?? ""
        }),

        // RAG Inspector Observability Telemetry Payload
        Telemetry = new
        {
            RetrievalConfidence = topConfidence,
            RerankLiftPercent = rerankLift,
            ContextRelevancyPercent = Math.Round(triadScore.ContextRelevancy * 100, 1),
            FaithfulnessPercent = Math.Round(triadScore.Faithfulness * 100, 1),
            AnswerRelevancyPercent = Math.Round(triadScore.AnswerRelevancy * 100, 1),
            
            DosageGuard = new
            {
                IsValid = dosageCheck.IsValid,
                ExtractedDosages = dosageCheck.ExtractedDosagesInAnswer,
                MissingDosages = dosageCheck.MissingDosages,
                Status = dosageCheck.StatusMessage
            },

            GuardrailStatus = (triadScore.Faithfulness >= 0.70f && dosageCheck.IsValid) ? "SUCCESS / PASSED" : "BLOCKED / WARNING",
            
            LatencyMs = new
            {
                Ingest = Math.Round(ingestTimer.Elapsed.TotalMilliseconds, 2),
                Bm25 = Math.Round(bm25Timer.Elapsed.TotalMilliseconds, 2),
                SimdVector = Math.Round(denseTimer.Elapsed.TotalMilliseconds, 2),
                GpuRerank = Math.Round(gpuTimer.Elapsed.TotalMilliseconds, 2),
                LlmGenerate = Math.Round(llmTimer.Elapsed.TotalMilliseconds, 2),
                Total = Math.Round(totalTimer.Elapsed.TotalMilliseconds, 2)
            },
            LlmModel = ollamaModel,

            RetrievedChunks = rerankedResults.Select(r => new
            {
                Doc = r.Chunk.DocumentId,
                Content = r.Chunk.Content,
                Score = Math.Round(r.Score, 4),
                Rank = r.Rank,
                Engine = r.RetrievalType
            })
        }
    });
});

app.Run();

// Geometric & Distance Helpers
static float CosineSim(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
{
    if (a.Length != b.Length || a.Length == 0) return 0;
    var spanA = a.Span;
    var spanB = b.Span;
    float dot = 0, normA = 0, normB = 0;
    for (int i = 0; i < spanA.Length; i++)
    {
        dot += spanA[i] * spanB[i];
        normA += spanA[i] * spanA[i];
        normB += spanB[i] * spanB[i];
    }
    float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
    return denom > 1e-6f ? Math.Clamp(dot / denom, 0f, 1f) : 0f;
}

static float EuclideanDist(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
{
    if (a.Length != b.Length || a.Length == 0) return 0;
    var spanA = a.Span;
    var spanB = b.Span;
    float sum = 0;
    for (int i = 0; i < spanA.Length; i++)
    {
        float diff = spanA[i] - spanB[i];
        sum += diff * diff;
    }
    return MathF.Sqrt(sum);
}

// Request Records
public class LabEvaluationRequest
{
    public string Query { get; set; } = string.Empty;
    public string Corpus { get; set; } = string.Empty;
    public string? CandidateResponse { get; set; }
    public int MaxTokensPerChunk { get; set; } = 256;
    public int OverlapTokens { get; set; } = 32;
    public int RrfK { get; set; } = 60;
    public float RerankThreshold { get; set; } = 0.25f;
    public int Dimensions { get; set; } = 16;
}

public class ChatRagRequest
{
    public string Query { get; set; } = string.Empty;
    public string? Corpus { get; set; }
    public string? DocumentName { get; set; }
    public string? CandidateResponseOverride { get; set; }
}
