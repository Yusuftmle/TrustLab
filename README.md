# TrustLab — Deterministic RAG & Guardrail Research Engine

A high-performance **C# (.NET 9 / .NET 10)** research workspace built to eliminate LLM hallucinations to absolute zero in production environments through **Clean Architecture**, **SIMD-accelerated Hybrid Search**, **N-gram Grounding Guards**, **Deterministic JSON Schema Enforcers**, and **Circuit Breaker Fallbacks**.

---

## 🏛️ System Architecture

```
TrustLab/
├── TrustLab.slnx
├── src/
│   ├── TrustLab.Domain/           # Entity records, Result<T> monad, Tokenizer, Error models
│   ├── TrustLab.Application/      # Use cases, Contracts, Guardrail & RAG interfaces
│   ├── TrustLab.Rag/              # Semantic Chunker, BM25 Index, SIMD Dense Store, RRF & Reranker
│   ├── TrustLab.Guardrails/       # JSON Schema Enforcer & Auto-Repair, N-Gram Grounding Guard, Circuit Breaker
│   ├── TrustLab.Agents/           # State-Driven Supervisor with Critic Feedback & Self-Correction
│   ├── TrustLab.Telemetry/        # Nanosecond & Millisecond ExecutionTracer, Token tracking
│   └── TrustLab.Infrastructure/   # Deterministic Embedder, Controllable LLM Mock clients
├── tests/
│   ├── TrustLab.UnitTests/        # 9 Unit tests (Chunker, BM25, SIMD Cosine, RRF, Cross-Encoder, Schema, Grounding)
│   └── TrustLab.IntegrationTests/ # 4 Integration tests (Grounded pass, Self-correction, Circuit breaker, Deficit)
└── benchmarks/
    └── TrustLab.Benchmarks/       # Quantitative benchmark runner (Faithfulness, Recall, Zero-Hallucination SLA)
```

---

## ⚡ Key Technical Innovations

1. **SIMD-Accelerated Hybrid Search (`TrustLab.Rag`):**
   - **Sparse Search**: In-memory Okapi BM25 index with stemmed tokenization and dynamic IDF calculation.
   - **Dense Search**: SIMD vector cosine similarity powered by `System.Numerics.Tensors.TensorPrimitives.CosineSimilarity`.
   - **Fusion**: Reciprocal Rank Fusion (RRF) with constant $k=60$.
   - **Re-Ranking Filter**: Cross-Encoder relevance heuristic combining stemmed query coverage, sequential bigram affinity, and prior rank signals with strict noise rejection thresholds.

2. **Zero-Hallucination Deterministic Guardrails (`TrustLab.Guardrails`):**
   - **JSON Schema Enforcer**: Validates outputs against strongly-typed C# records and applies deterministic auto-repair for common LLM syntax flaws (unclosed brackets, trailing commas, single-quote keys, markdown code fences).
   - **N-Gram Grounding Guard**: Factual sentence-by-sentence verification measuring unigram and bigram support against retrieved context chunks. Rejects any ungrounded assertions.
   - **Deterministic Circuit Breaker**: Intercepts persistent hallucinations or context deficits and dispatches safe, auditable fallback responses.

3. **Closed-Loop Multi-Agent Orchestration (`TrustLab.Agents`):**
   - **Finite State Machine**: `Idle` ➔ `Retrieving` ➔ `Synthesizing` ➔ `Validating` ➔ `Refining` (Critic Loop) ➔ `Completed` / `FallbackDispatched`.
   - **Self-Correction**: Feeds specific grounding violations back to the generator for targeted claim correction before hitting the retry budget.

---

## 🧪 Verification & Benchmarks

### Run Test Suite
```bash
dotnet test TrustLab.slnx --logger "console;verbosity=normal"
```
**Results:** 13/13 Passed (100% Pass Rate).

### Run Quantitative Benchmark
```bash
dotnet run --project benchmarks/TrustLab.Benchmarks
```
**Results:** Benchmark Gate Status: 100% of defined scenarios met deterministic criteria (4/4 in test set scope).

---

## 🛠️ Requirements
- **.NET SDK:** 9.0 or 10.0
- **OS:** Windows / Linux / macOS (Cross-platform)
