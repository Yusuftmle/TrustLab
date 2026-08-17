// TrustLab Scientific RAG & Embedding Workbench (Apache ECharts + Vis.js Network)

let vectorChartInstance = null;
let heatmapChartInstance = null;
let rankingChartInstance = null;
let networkInstance = null;

// Presets
const presets = {
  hybrid_match: {
    query: "TrustLab guardrail mimarisi sıfır halüsinasyon sağlar",
    corpus: `Doküman 1: TrustLab mimarisi deterministik guardrail bileşenleri ile sıfır halüsinasyon hedefleyen C# .NET 9 araştırma motorudur.
Doküman 2: Okapi BM25 sparse arama ve SIMD Cosine Similarity vektör benzerliği Reciprocal Rank Fusion ile birleştirilir.
Doküman 3: İtalyan makarnası pişirirken suyun kaynaması ve tuzlanması gerekir.`,
    candidate: "TrustLab mimarisi deterministik guardrail bileşenleri ile sıfır halüsinasyon hedefleyen bir sistemdir. Kullanıcı verilerini Oracle veritabanında saklar."
  },
  exact_keyword: {
    query: "CS0234 namespace TrustLab Rag eksik assembly hatası",
    corpus: `Doküman 1: Derleyici hatası CS0234: The type or namespace name 'Rag' does not exist in the namespace 'TrustLab' assembly referansı eklenerek çözülür.
Doküman 2: C# projelerinde Clean Architecture katmanları arasındaki bağımlılıklar csproj referanslarıyla kurulur.
Doküman 3: BM25 algoritması nadir anahtar kelimeleri ve hata kodlarını yüksek IDF değeri ile ödüllendirir.`,
    candidate: "CS0234 hatası TrustLab Rag projesine assembly referansı eklenerek çözülür."
  },
  hallucination_trap: {
    query: "TrustLab hangi bulut veritabanında kullanıcı şifrelerini tutar?",
    corpus: `Doküman 1: TrustLab mimarisi tamamen in-memory vektör indeksleri ve yerel disk tabanlı BM25 depoları kullanır.
Doküman 2: Güvenilirlik testleri için ExecutionTracer sınıfı milisaniye bazında gecikme ve token denetimi yapar.
Doküman 3: Deterministik devre kesici, desteksiz iddialarda doğrudan güvenli fallback yanıtı üretir.`,
    candidate: "TrustLab kullanıcı şifrelerini AWS DynamoDB ve Redis veritabanında 256-bit AES ile şifreleyerek tutar."
  }
};

function loadExperimentPreset(presetKey) {
  document.querySelectorAll('.preset-btn').forEach(b => b.classList.remove('active'));
  event.target.classList.add('active');

  const p = presets[presetKey];
  if (p) {
    document.getElementById('queryInput').value = p.query;
    document.getElementById('corpusInput').value = p.corpus;
    document.getElementById('candidateResponseInput').value = p.candidate;
    executeFullPipeline();
  }
}

function updateParams() {
  document.getElementById('rrfKVal').textContent = document.getElementById('rrfKRange').value;
  document.getElementById('rerankThresholdVal').textContent = document.getElementById('rerankThresholdRange').value;
  document.getElementById('embedDimVal').textContent = document.getElementById('embedDimRange').value + "D";
  executeFullPipeline();
}

// --- Mathematical Helpers ---
function tokenize(text) {
  if (!text) return [];
  return text.toLowerCase().match(/\b[\w\-]{2,}\b/g) || [];
}

const StopWords = new Set(["the", "is", "at", "which", "on", "a", "an", "and", "or", "in", "with", "to", "for", "of", "as", "by", "that", "this", "it", "from", "be", "are", "was", "were", "bu", "bir", "ve", "ile", "için", "olan", "olarak", "veya", "gerekir"]);

function stemWord(word) {
  if (!word || word.length <= 3) return word;
  let w = word.toLowerCase();
  if (w.endsWith("ler") || w.endsWith("lar")) return w.slice(0, -3);
  if (w.endsWith("dir") || w.endsWith("dur") || w.endsWith("tir") || w.endsWith("tur")) return w.slice(0, -3);
  if (w.endsWith("nin") || w.endsWith("nın") || w.endsWith("den") || w.endsWith("dan")) return w.slice(0, -3);
  if (w.endsWith("si") || w.endsWith("sı")) return w.slice(0, -2);
  if (w.endsWith("s") && !w.endsWith("ss")) return w.slice(0, -1);
  return w;
}

// High-dimensional deterministic vector embedding simulation
function generateEmbedding(text, dimensions = 16) {
  const vec = new Float32Array(dimensions);
  const tokens = tokenize(text);
  
  tokens.forEach(t => {
    let hash = 0;
    for (let i = 0; i < t.length; i++) {
      hash = (hash << 5) - hash + t.charCodeAt(i);
      hash |= 0;
    }
    for (let d = 0; d < dimensions; d++) {
      const sign = (hash & (1 << (d % 16))) !== 0 ? 1.0 : -1.0;
      vec[d] += sign * (1.0 + Math.abs(hash % 100) / 100.0);
    }
  });

  // L2 Normalize
  let sumSq = 0;
  for (let i = 0; i < dimensions; i++) sumSq += vec[i] * vec[i];
  const norm = Math.sqrt(sumSq);
  if (norm > 1e-6) {
    for (let i = 0; i < dimensions; i++) vec[i] /= norm;
  }
  return Array.from(vec);
}

function cosineSimilarity(vecA, vecB) {
  let dot = 0;
  for (let i = 0; i < vecA.length; i++) dot += vecA[i] * vecB[i];
  return Math.max(0, Math.min(1.0, dot));
}

// --- Main Pipeline Execution & Visualization ---
function executeFullPipeline() {
  const query = document.getElementById('queryInput').value.trim();
  const corpusRaw = document.getElementById('corpusInput').value.trim();
  const rrfK = parseInt(document.getElementById('rrfKRange').value, 10);
  const rerankThreshold = parseFloat(document.getElementById('rerankThresholdRange').value);
  const dimensions = parseInt(document.getElementById('embedDimRange').value, 10);

  if (!query || !corpusRaw) return;

  const docs = corpusRaw.split('\n').filter(l => l.trim().length > 0).map((content, idx) => ({
    id: `Doc_${idx + 1}`,
    name: content.split(':')[0] || `Doc_${idx + 1}`,
    content: content
  }));

  // 1. Tokenize & Stems
  const queryTokens = tokenize(query);
  const queryStems = queryTokens.map(stemWord);
  const queryContentStems = queryTokens.filter(t => !StopWords.has(t)).map(stemWord);

  // 2. BM25 Calculation
  const docTokensList = docs.map(d => tokenize(d.content));
  const docStemsList = docTokensList.map(tokens => tokens.map(stemWord));
  const nDocs = docs.length;
  const avgdl = docTokensList.reduce((acc, curr) => acc + curr.length, 0) / Math.max(1, nDocs);
  const k1 = 1.5;
  const b = 0.75;

  const bm25Scores = docs.map((doc, dIdx) => {
    const docStems = docStemsList[dIdx];
    const docLen = docStems.length;
    let score = 0;

    queryContentStems.forEach(term => {
      const tf = docStems.filter(s => s === term).length;
      if (tf > 0) {
        const df = docStemsList.filter(ds => ds.includes(term)).length;
        const idf = Math.log(1 + (nDocs - df + 0.5) / (df + 0.5));
        const num = tf * (k1 + 1);
        const den = tf + k1 * (1 - b + b * (docLen / avgdl));
        score += Math.max(0, idf * (num / den));
      }
    });
    return { doc, score: parseFloat(score.toFixed(4)) };
  });

  // 3. Dense Cosine Vectors
  const queryVector = generateEmbedding(query, dimensions);
  const docVectors = docs.map(d => generateEmbedding(d.content, dimensions));
  const denseScores = docs.map((doc, idx) => {
    const sim = cosineSimilarity(queryVector, docVectors[idx]);
    return { doc, vector: docVectors[idx], score: parseFloat(sim.toFixed(4)) };
  });

  // 4. Reciprocal Rank Fusion (RRF)
  const sortedBm25 = [...bm25Scores].sort((a, b) => b.score - a.score);
  const sortedDense = [...denseScores].sort((a, b) => b.score - a.score);

  const rrfResults = docs.map(doc => {
    const rankSparse = sortedBm25.findIndex(s => s.doc.id === doc.id) + 1;
    const rankDense = sortedDense.findIndex(s => s.doc.id === doc.id) + 1;
    const rrfScore = (1.0 / (rrfK + rankSparse)) + (1.0 / (rrfK + rankDense));
    return {
      doc,
      rankSparse,
      rankDense,
      rrfScore: parseFloat(rrfScore.toFixed(5)),
      bm25: sortedBm25.find(s => s.doc.id === doc.id).score,
      cosine: sortedDense.find(s => s.doc.id === doc.id).score
    };
  }).sort((a, b) => b.rrfScore - a.rrfScore);

  // 5. Cross-Encoder Reranking
  const rerankedResults = rrfResults.map((item, idx) => {
    const docStems = docStemsList.find((_, i) => docs[i].id === item.doc.id);
    const matched = queryContentStems.filter(qt => docStems.includes(qt)).length;
    const coverage = queryContentStems.length > 0 ? matched / queryContentStems.length : 0;
    const rerankScore = (coverage * 0.55) + (item.cosine * 0.25) + ((1.0 / (idx + 1)) * 0.20);
    const isPassed = rerankScore >= rerankThreshold;
    return {
      ...item,
      coverage: parseFloat((coverage * 100).toFixed(1)),
      rerankScore: parseFloat(rerankScore.toFixed(3)),
      isPassed
    };
  }).sort((a, b) => b.rerankScore - a.rerankScore);

  // --- RENDER VISUALIZATIONS ---
  render2DVectorSpace(query, queryVector, docs, docVectors, denseScores);
  renderForceGraph(query, docs, bm25Scores, denseScores, rrfResults);
  renderTfidfHeatmap(queryContentStems, docs, docStemsList);
  renderRankingComparison(rerankedResults);
  evaluateLiveGrounding();
}

// --- 1. Render 2D Vector Embedding Space (PCA Projection) ---
function render2DVectorSpace(query, queryVector, docs, docVectors, denseScores) {
  const chartDom = document.getElementById('vectorSpaceChart');
  if (!vectorChartInstance) {
    vectorChartInstance = echarts.init(chartDom);
  }

  // Simple 2D PCA projection approximation (1st two principal components)
  function project2D(vec) {
    let x = 0, y = 0;
    for (let i = 0; i < vec.length; i++) {
      x += vec[i] * Math.cos((i * 2 * Math.PI) / vec.length);
      y += vec[i] * Math.sin((i * 2 * Math.PI) / vec.length);
    }
    return [parseFloat(x.toFixed(3)), parseFloat(y.toFixed(3))];
  }

  const queryPoint = project2D(queryVector);
  const docPoints = docs.map((doc, idx) => {
    const p = project2D(docVectors[idx]);
    const sim = denseScores[idx].score;
    return {
      name: doc.name,
      value: [p[0], p[1], sim],
      content: doc.content
    };
  });

  const linesData = docPoints.map(dp => ({
    coords: [queryPoint, [dp.value[0], dp.value[1]]],
    lineStyle: {
      color: dp.value[2] > 0.6 ? '#10b981' : dp.value[2] > 0.3 ? '#6366f1' : '#64748b',
      width: Math.max(1, dp.value[2] * 4),
      type: 'dashed'
    }
  }));

  const option = {
    backgroundColor: '#060911',
    tooltip: {
      trigger: 'item',
      backgroundColor: '#0c1222',
      borderColor: '#6366f1',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code', fontSize: 12 },
      formatter: function (params) {
        if (params.seriesType === 'scatter') {
          if (params.data.name === 'QUERY') {
            return `<strong>🔍 QUERY (Sorgu)</strong><br/>Koordinat: [${params.data.value[0]}, ${params.data.value[1]}]`;
          }
          return `<strong>📄 ${params.data.name}</strong><br/>Cosine Benzerliği: <span style="color:#10b981;font-weight:bold;">${(params.data.value[2] * 100).toFixed(1)}%</span><br/>Koordinat: [${params.data.value[0]}, ${params.data.value[1]}]`;
        }
      }
    },
    grid: { left: '10%', right: '10%', top: '15%', bottom: '10%' },
    xAxis: {
      type: 'value',
      name: 'PCA Boyut 1',
      nameTextStyle: { color: '#64748b' },
      splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } },
      axisLine: { lineStyle: { color: '#64748b' } }
    },
    yAxis: {
      type: 'value',
      name: 'PCA Boyut 2',
      nameTextStyle: { color: '#64748b' },
      splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } },
      axisLine: { lineStyle: { color: '#64748b' } }
    },
    series: [
      {
        type: 'lines',
        coordinateSystem: 'cartesian2d',
        data: linesData,
        effect: { show: true, period: 4, trailLength: 0.2, symbol: 'arrow', symbolSize: 6, color: '#06b6d4' }
      },
      {
        name: 'Query',
        type: 'scatter',
        symbol: 'diamond',
        symbolSize: 22,
        itemStyle: { color: '#06b6d4', shadowBlur: 15, shadowColor: 'rgba(6,182,212,0.8)' },
        data: [{ name: 'QUERY', value: queryPoint }]
      },
      {
        name: 'Documents',
        type: 'scatter',
        symbolSize: function (val) { return 14 + (val[2] * 18); },
        itemStyle: {
          color: function (params) {
            const sim = params.data.value[2];
            return sim > 0.6 ? '#10b981' : sim > 0.3 ? '#6366f1' : '#f43f5e';
          },
          shadowBlur: 10,
          shadowColor: 'rgba(99,102,241,0.5)'
        },
        label: {
          show: true,
          formatter: '{b}',
          position: 'top',
          color: '#cbd5e1',
          fontFamily: 'Plus Jakarta Sans',
          fontWeight: 'bold',
          fontSize: 11
        },
        data: docPoints
      }
    ]
  };

  vectorChartInstance.setOption(option);
}

// --- 2. Render Vis.js Force-Directed Interactive Graph ---
function renderForceGraph(query, docs, bm25Scores, denseScores, rrfResults) {
  const container = document.getElementById('similarityNetworkGraph');
  
  const nodes = [
    {
      id: 0,
      label: "🔍 QUERY\n" + query.slice(0, 25) + "...",
      shape: "hexagon",
      color: { background: "#06b6d4", border: "#38bdf8" },
      font: { color: "#ffffff", face: "Plus Jakarta Sans", size: 14, bold: true },
      size: 35,
      shadow: true
    }
  ];

  const edges = [];

  docs.forEach((doc, idx) => {
    const docId = idx + 1;
    const rrf = rrfResults.find(r => r.doc.id === doc.id);
    const score = rrf ? rrf.rrfScore * 100 : 10;
    const cosine = denseScores[idx].score;
    const bm25 = bm25Scores[idx].score;

    nodes.push({
      id: docId,
      label: `📄 ${doc.name}\nCos: ${(cosine*100).toFixed(0)}% | BM25: ${bm25}`,
      shape: "box",
      color: {
        background: cosine > 0.5 ? "#1e293b" : "#0f172a",
        border: cosine > 0.5 ? "#10b981" : "#6366f1"
      },
      font: { color: "#e2e8f0", face: "Fira Code", size: 12 },
      margin: 10,
      shadow: true
    });

    edges.push({
      from: 0,
      to: docId,
      value: Math.max(1, (cosine * 10) + (bm25 > 0 ? 3 : 0)),
      color: { color: cosine > 0.5 ? "#10b981" : "#6366f1", highlight: "#06b6d4" },
      arrows: "to",
      dashes: cosine < 0.2,
      smooth: { type: "continuous" }
    });
  });

  const data = { nodes: new vis.DataSet(nodes), edges: new vis.DataSet(edges) };
  const options = {
    physics: {
      stabilization: false,
      barnesHut: { gravitationalConstant: -3000, springConstant: 0.04, springLength: 140 }
    },
    interaction: {
      hover: true,
      tooltipDelay: 100,
      zoomView: false, // Sayfa scroll'unun yakalanmasını ve takılmasını engeller
      dragView: true
    }
  };

  if (networkInstance) {
    networkInstance.setData(data);
  } else {
    networkInstance = new vis.Network(container, data, options);
  }
}

// --- 3. Render TF-IDF Heatmap Matrix (Apache ECharts) ---
function renderTfidfHeatmap(queryTerms, docs, docStemsList) {
  const chartDom = document.getElementById('tfidfHeatmapChart');
  if (!heatmapChartInstance) {
    heatmapChartInstance = echarts.init(chartDom);
  }

  const terms = queryTerms.length > 0 ? queryTerms : ["query_stem"];
  const docNames = docs.map(d => d.name);
  const data = [];

  terms.forEach((term, tIdx) => {
    docs.forEach((doc, dIdx) => {
      const docStems = docStemsList[dIdx];
      const count = docStems.filter(s => s === term).length;
      data.push([tIdx, dIdx, count]);
    });
  });

  const option = {
    backgroundColor: '#060911',
    tooltip: {
      position: 'top',
      backgroundColor: '#0c1222',
      borderColor: '#6366f1',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code' },
      formatter: function (p) {
        return `Kök: <strong>${terms[p.value[0]]}</strong><br/>Doküman: <strong>${docNames[p.value[1]]}</strong><br/>Frekans (TF): <strong>${p.value[2]}</strong>`;
      }
    },
    grid: { height: '65%', top: '15%', left: '15%', right: '10%' },
    xAxis: {
      type: 'category',
      data: terms,
      splitArea: { show: true },
      axisLabel: { color: '#94a3b8', fontFamily: 'Fira Code' }
    },
    yAxis: {
      type: 'category',
      data: docNames,
      splitArea: { show: true },
      axisLabel: { color: '#94a3b8', fontFamily: 'Fira Code' }
    },
    visualMap: {
      min: 0,
      max: Math.max(3, ...data.map(d => d[2])),
      calculable: true,
      orient: 'horizontal',
      left: 'center',
      bottom: '2%',
      inRange: { color: ['#1e293b', '#6366f1', '#10b981'] },
      textStyle: { color: '#94a3b8' }
    },
    series: [{
      name: 'TF-IDF Matrix',
      type: 'heatmap',
      data: data,
      label: { show: true, color: '#fff', fontFamily: 'Fira Code', fontWeight: 'bold' },
      emphasis: { itemStyle: { shadowBlur: 10, shadowColor: 'rgba(0, 0, 0, 0.5)' } }
    }]
  };

  heatmapChartInstance.setOption(option);
}

// --- 4. Render Ranking Comparison Waterfall (Apache ECharts) ---
function renderRankingComparison(rerankedResults) {
  const chartDom = document.getElementById('rankingComparisonChart');
  if (!rankingChartInstance) {
    rankingChartInstance = echarts.init(chartDom);
  }

  const docNames = rerankedResults.map(r => r.doc.name);
  const bm25Values = rerankedResults.map(r => r.bm25);
  const cosineValues = rerankedResults.map(r => r.cosine);
  const rerankScores = rerankedResults.map(r => r.rerankScore);

  const option = {
    backgroundColor: '#060911',
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: '#0c1222',
      borderColor: '#6366f1',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code' }
    },
    legend: {
      data: ['BM25 Sparse Skoru', 'SIMD Cosine Benzerliği', 'Cross-Encoder Rerank Skoru'],
      textStyle: { color: '#94a3b8' },
      top: '5%'
    },
    grid: { left: '8%', right: '8%', bottom: '10%', top: '22%', containLabel: true },
    xAxis: {
      type: 'value',
      axisLabel: { color: '#94a3b8', fontFamily: 'Fira Code' },
      splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } }
    },
    yAxis: {
      type: 'category',
      data: docNames,
      axisLabel: { color: '#f8fafc', fontFamily: 'Fira Code', fontWeight: 'bold' }
    },
    series: [
      {
        name: 'BM25 Sparse Skoru',
        type: 'bar',
        itemStyle: { color: '#f59e0b', borderRadius: [0, 4, 4, 0] },
        data: bm25Values
      },
      {
        name: 'SIMD Cosine Benzerliği',
        type: 'bar',
        itemStyle: { color: '#a855f7', borderRadius: [0, 4, 4, 0] },
        data: cosineValues
      },
      {
        name: 'Cross-Encoder Rerank Skoru',
        type: 'bar',
        itemStyle: { color: '#10b981', borderRadius: [0, 4, 4, 0] },
        data: rerankScores
      }
    ]
  };

  rankingChartInstance.setOption(option);
}

// --- 5. Live Grounding Inspector ---
function evaluateLiveGrounding() {
  const candidate = document.getElementById('candidateResponseInput').value.trim();
  const corpus = document.getElementById('corpusInput').value.trim();
  const banner = document.getElementById('groundingVerdictBanner');
  const proofsList = document.getElementById('sentenceProofList');

  if (!candidate || !corpus) return;

  const corpusStems = tokenize(corpus).filter(t => !StopWords.has(t)).map(stemWord);
  const sentences = candidate.split(/(?<=[.!?])\s+/).filter(s => s.trim().length > 0);

  let groundedCount = 0;
  let proofsHtml = '';

  sentences.forEach((sentence, idx) => {
    const sStems = tokenize(sentence).filter(t => !StopWords.has(t)).map(stemWord);
    const matched = sStems.filter(s => corpusStems.includes(s));
    const ratio = sStems.length > 0 ? matched.length / sStems.length : 1.0;
    const isGrounded = ratio >= 0.50;

    if (isGrounded) groundedCount++;

    proofsHtml += `
      <div class="proof-item ${isGrounded ? 'grounded' : 'ungrounded'}">
        <div class="proof-claim">Cümle ${idx + 1}: "${sentence}"</div>
        <div class="proof-support">
          <span>Kanıt Eşleşmesi: ${matched.length}/${sStems.length} kavram (${(ratio*100).toFixed(0)}%)</span>
          <span class="proof-tag ${isGrounded ? 'green' : 'red'}">${isGrounded ? '✅ DOĞRULANDI' : '⚠️ HALÜSİNASYON'}</span>
        </div>
      </div>
    `;
  });

  const faithfulness = sentences.length > 0 ? (groundedCount / sentences.length) : 1.0;
  const isPassed = faithfulness >= 0.80 && groundedCount === sentences.length;

  banner.className = `verdict-banner-box ${isPassed ? 'pass' : 'fail'}`;
  banner.innerHTML = isPassed
    ? `<span>✅ DETERMINISTIK GATE: GEÇTİ (Sadakat: ${(faithfulness*100).toFixed(0)}% - Tüm iddialar doğrulanabilir kaynakta mevcut)</span>`
    : `<span>⚠️ DETERMINISTIK GATE: REDDEDİLDİ (Sadakat: ${(faithfulness*100).toFixed(0)}% - Kaynakta olmayan iddialar tespit edildi)</span>`;

  proofsList.innerHTML = proofsHtml;
}

// Window Resize Handler
window.addEventListener('resize', () => {
  if (vectorChartInstance) vectorChartInstance.resize();
  if (heatmapChartInstance) heatmapChartInstance.resize();
  if (rankingChartInstance) rankingChartInstance.resize();
});

// Initial Execute on Load
document.addEventListener('DOMContentLoaded', () => {
  executeFullPipeline();
});
