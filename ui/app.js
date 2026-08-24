// TrustLab Scientific RAG Workbench & Clinical Observability Assistant
// Integrated with C# .NET 10 API, RTX 4060 Ti GPU & Grounding Guardrails

let vectorChartInstance = null;
let triadChartInstance = null;
let hardwareChartInstance = null;

const API_BASE = "http://localhost:5000";
let activeView = "lab"; // 'lab' or 'chat'
let currentMessageTelemetry = null;

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function escapeRegex(string) {
  if (!string) return '';
  return String(string).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// Scientific Presets for Lab Mode
const presets = {
  hybrid_match: {
    query: "Penisilin alerjisinde hangi antibiyotik kontrendikedir?",
    corpus: `Doküman 1: Şiddetli penisilin anafilaksi öyküsü olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir.
Doküman 2: Alternatif olarak makrolid grubu antibiyotikler (Klaritromisin, Azitromisin) güvenle tercih edilebilir.
Doküman 3: İtalyan mutfağında spagetti yaparken tenceredeki su kaynadıktan sonra tuz atılmalıdır.`,
    candidate: "Şiddetli penisilin alerjisi olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir. Alternatif olarak makrolid grubu antibiyotikler güvenle tercih edilebilir."
  },
  exact_keyword: {
    query: "CS0234 namespace TrustLab Rag eksik assembly hatası",
    corpus: `Doküman 1: Derleyici hatası CS0234: The type or namespace name 'Rag' does not exist in the namespace 'TrustLab' assembly referansı eklenerek çözülür.
Doküman 2: C# projelerinde Clean Architecture katmanları arasındaki bağımlılıklar csproj referanslarıyla kurulur.
Doküman 3: BM25 algoritması nadir anahtar kelimeleri ve hata kodlarını yüksek IDF değeri ile ödüllendirir.`,
    candidate: "CS0234 hatası TrustLab Rag projesine assembly referansı eklenerek çözülür."
  },
  hallucination_trap: {
    query: "TrustLab kullanıcı şifrelerini hangi bulut veritabanında saklar?",
    corpus: `Doküman 1: TrustLab mimarisi tamamen yerel disk tabanlı in-memory vektör indeksleri ve BM25 depoları kullanır.
Doküman 2: Güvenilirlik testleri için ExecutionTracer sınıfı milisaniye bazında gecikme ve token denetimi yapar.
Doküman 3: Deterministik devre kesici, desteksiz iddialarda doğrudan güvenli fallback yanıtı üretir.`,
    candidate: "TrustLab kullanıcı şifrelerini AWS DynamoDB ve Redis veritabanında 256-bit AES ile şifreleyerek bulutta saklar."
  }
};

// 1. Health Check for C# Backend
async function checkApiHealth() {
  const badge = document.getElementById('apiStatusBadge');
  const text = document.getElementById('apiStatusText');
  try {
    const res = await fetch(`${API_BASE}/api/system/status`, { signal: AbortSignal.timeout(5000) });
    if (res.ok) {
      const data = await res.json();
      badge.className = "api-status-badge";
      // API camelCase döndürür: gpuDevice, simdHardwareAcceleration, onnxModelLoaded
      const simd = data.simdHardwareAcceleration ? '⚡ SIMD AVX' : 'CPU';
      const gpu = data.gpuDevice || 'CPU Fallback';
      const onnx = data.onnxModelLoaded ? '🧠 ONNX' : '';
      text.textContent = `🟢 .NET 10 | ${gpu} | ${simd} ${onnx}`;
      return true;
    } else {
      throw new Error('API non-200');
    }
  } catch (err) {
    badge.className = "api-status-badge offline";
    text.textContent = "🔴 C# API Kapalı — " + err.message;
    return false;
  }
}

// 2. View Switcher: Lab Mode vs. Clinical Chat Mode
function switchViewMode(mode) {
  activeView = mode;
  const labTab = document.getElementById('tabLabMode');
  const chatTab = document.getElementById('tabChatMode');
  const labContainer = document.getElementById('labViewContainer');
  const chatContainer = document.getElementById('chatViewContainer');

  if (mode === 'lab') {
    labTab.classList.add('active');
    chatTab.classList.remove('active');
    labContainer.style.display = 'flex';
    chatContainer.style.display = 'none';
    setTimeout(() => {
      if (vectorChartInstance) vectorChartInstance.resize();
      if (triadChartInstance) triadChartInstance.resize();
      if (hardwareChartInstance) hardwareChartInstance.resize();
    }, 100);
  } else {
    chatTab.classList.add('active');
    labTab.classList.remove('active');
    labContainer.style.display = 'none';
    chatContainer.style.display = 'flex';
  }
}

function loadExperimentPreset(presetKey) {
  document.querySelectorAll('.preset-btn').forEach(b => b.classList.remove('active'));
  if (event && event.target) event.target.classList.add('active');

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

// --- Lab Mode: Main Pipeline Execution ---
async function executeFullPipeline() {
  const query = document.getElementById('queryInput').value.trim();
  const corpusRaw = document.getElementById('corpusInput').value.trim();
  const candidate = document.getElementById('candidateResponseInput').value.trim();
  const rrfK = parseInt(document.getElementById('rrfKRange').value, 10);
  const rerankThreshold = parseFloat(document.getElementById('rerankThresholdRange').value);
  const dimensions = parseInt(document.getElementById('embedDimRange').value, 10);

  if (!query || !corpusRaw) return;

  try {
    const response = await fetch(`${API_BASE}/api/lab/evaluate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        query: query,
        corpus: corpusRaw,
        candidateResponse: candidate,
        rrfK: rrfK,
        rerankThreshold: rerankThreshold,
        dimensions: dimensions,
        maxTokensPerChunk: 256,
        overlapTokens: 32
      })
    });

    if (response.ok) {
      const data = await response.json();
      renderScientificLabResults(data);
    }
  } catch (err) {
    console.error("Evaluation API Error:", err);
  }
}

function renderScientificLabResults(data) {
  render2DVectorSpace(data.query, data.queryVector, data.docVectors);
  renderRagTriadChart(data.ragTriad);
  renderHardwareAndRankingChart(data.hardwareProfiling, data.rankingMetrics);
  renderGroundingProofExplorer(data.ragTriad.sentenceDetails, data.ragTriad.faithfulness);
}

// --- Panel 1: 2D Vector Space (Semantic Polar Orbit & L2 Geometri) ---
function render2DVectorSpace(query, queryVector, docVectors) {
  const chartDom = document.getElementById('vectorSpaceChart');
  if (!vectorChartInstance) {
    vectorChartInstance = echarts.init(chartDom);
  }

  // Sorgu her zaman merkezde (0, 0)
  const queryPoint = [0, 0];
  const numDocs = (docVectors || []).length || 1;

  const docPoints = (docVectors || []).map((dv, idx) => {
    // Cosine mesafe yarıçap (r), açı eşit aralıklı dağılım
    const r = Math.max(0.08, dv.cosineDistance ?? 0.5);
    const theta = (idx / numDocs) * 2 * Math.PI - (Math.PI / 2);
    const x = parseFloat((r * Math.cos(theta)).toFixed(3));
    const y = parseFloat((r * Math.sin(theta)).toFixed(3));

    return {
      name: dv.documentId || `Chunk ${idx + 1}`,
      value: [x, y, dv.cosineDistance ?? 0, dv.euclideanDistance ?? 0],
      content: dv.content || ''
    };
  });

  const linesData = docPoints.map(dp => ({
    coords: [queryPoint, [dp.value[0], dp.value[1]]],
    lineStyle: {
      color: dp.value[2] < 0.4 ? '#10b981' : dp.value[2] < 0.7 ? '#6366f1' : '#64748b',
      width: Math.max(1, (1.0 - dp.value[2]) * 3),
      type: 'dashed'
    }
  }));

  const option = {
    backgroundColor: '#060911',
    tooltip: {
      trigger: 'item',
      backgroundColor: '#0c1222',
      borderColor: '#6366f1',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code', fontSize: 11 },
      formatter: function (params) {
        if (params.seriesType === 'scatter') {
          if (params.data.name === 'QUERY') {
            return `<strong>🔍 CANLI SORGU (QUERY)</strong><br/>"${(query || '').slice(0, 50)}..."<br/>Merkez: [0.0, 0.0]`;
          }
          return `<strong>📄 ${params.data.name}</strong><br/>
                  Cosine Mesafe (r): <span style="color:#10b981;font-weight:bold;">${params.data.value[2]}</span><br/>
                  Euclidean (L2): <span style="color:#06b6d4;font-weight:bold;">${params.data.value[3]}</span><br/>
                  Koordinat: [${params.data.value[0]}, ${params.data.value[1]}]<br/>
                  <span style="color:#94a3b8; font-size:10px;">${(params.data.content || '').slice(0, 80)}...</span>`;
        }
      }
    },
    grid: { left: '10%', right: '10%', top: '15%', bottom: '10%' },
    xAxis: {
      type: 'value',
      scale: true,
      name: 'Vektör X (Proj.)',
      nameTextStyle: { color: '#64748b' },
      splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } },
      axisLine: { lineStyle: { color: '#64748b' } }
    },
    yAxis: {
      type: 'value',
      scale: true,
      name: 'Vektör Y (Proj.)',
      nameTextStyle: { color: '#64748b' },
      splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } },
      axisLine: { lineStyle: { color: '#64748b' } }
    },
    dataZoom: [
      {
        type: 'inside',
        xAxisIndex: [0],
        yAxisIndex: [0],
        filterMode: 'none',
        moveOnMouseMove: true,
        zoomOnMouseWheel: true
      }
    ],
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
        label: {
          show: true,
          formatter: 'MERKEZ SORGU',
          position: 'bottom',
          color: '#38bdf8',
          fontFamily: 'Plus Jakarta Sans',
          fontWeight: 'bold',
          fontSize: 10
        },
        data: [{ name: 'QUERY', value: queryPoint }]
      },
      {
        name: 'Documents',
        type: 'scatter',
        symbolSize: function (val) { return 10 + ((1.0 - (val[2] || 0.5)) * 14); },
        itemStyle: {
          color: function (params) {
            const cosDist = params.data.value[2];
            return cosDist < 0.4 ? '#10b981' : cosDist < 0.7 ? '#6366f1' : '#f43f5e';
          },
          opacity: 0.88,
          shadowBlur: 10,
          shadowColor: 'rgba(99,102,241,0.5)'
        },
        label: {
          show: true,
          formatter: function (params) {
            // Yüzlerce chunk varsa sadece en yakın olanları etiketle, ekran kalabalık olmasın
            if (numDocs > 20) {
              return (params.data.value[2] < 0.45) ? params.data.name : '';
            }
            return params.data.name;
          },
          position: 'top',
          color: '#cbd5e1',
          fontFamily: 'Plus Jakarta Sans',
          fontWeight: 'bold',
          fontSize: 10
        },
        data: docPoints
      }
    ]
  };

  vectorChartInstance.setOption(option);
}

// --- Visualizer Fullscreen Toggle ---
function toggleFullscreenViz(boxId) {
  const box = document.getElementById(boxId);
  if (!box) return;

  const btn = box.querySelector('.viz-expand-btn');
  const isFull = box.classList.toggle('fullscreen-viz');

  if (btn) {
    btn.innerHTML = isFull ? '✕' : '⛶';
    btn.title = isFull ? 'Küçült' : 'Tam Ekran / Büyüt';
  }

  // ESC tuşuyla tam ekrandan çıkma desteği
  if (isFull) {
    const escHandler = (e) => {
      if (e.key === 'Escape') {
        box.classList.remove('fullscreen-viz');
        if (btn) {
          btn.innerHTML = '⛶';
          btn.title = 'Tam Ekran / Büyüt';
        }
        document.removeEventListener('keydown', escHandler);
        resizeAllCharts();
      }
    };
    document.addEventListener('keydown', escHandler);
  }

  resizeAllCharts();
}

function resizeAllCharts() {
  setTimeout(() => {
    if (vectorChartInstance) vectorChartInstance.resize();
    if (triadChartInstance) triadChartInstance.resize();
    if (hardwareChartInstance) hardwareChartInstance.resize();
  }, 80);
}

// --- Panel 2: RAG Triad Radar ---
function renderRagTriadChart(ragTriad) {
  const chartDom = document.getElementById('ragTriadChart');
  if (!triadChartInstance) {
    triadChartInstance = echarts.init(chartDom);
  }

  const cr = (ragTriad.ContextRelevancy || ragTriad.contextRelevancy || 0) * 100;
  const ft = (ragTriad.Faithfulness || ragTriad.faithfulness || 0) * 100;
  const ar = (ragTriad.AnswerRelevancy || ragTriad.answerRelevancy || 0) * 100;

  const option = {
    backgroundColor: '#060911',
    tooltip: {
      trigger: 'axis',
      backgroundColor: '#0c1222',
      borderColor: '#06b6d4',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code' }
    },
    radar: {
      indicator: [
        { name: `1. Context Relevancy\n(${cr.toFixed(0)}% Arama Hassasiyeti)`, max: 100 },
        { name: `2. Faithfulness\n(${ft.toFixed(0)}% Olgusal Sadakat)`, max: 100 },
        { name: `3. Answer Relevancy\n(${ar.toFixed(0)}% Soru-Yanıt Uyumu)`, max: 100 }
      ],
      shape: 'polygon',
      splitNumber: 4,
      axisName: { color: '#cbd5e1', fontFamily: 'Plus Jakarta Sans', fontWeight: 'bold', fontSize: 11 },
      splitLine: { lineStyle: { color: 'rgba(99, 102, 241, 0.2)' } },
      splitArea: { show: true, areaStyle: { color: ['rgba(6, 182, 212, 0.05)', 'rgba(99, 102, 241, 0.05)'] } },
      axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.1)' } }
    },
    series: [{
      name: 'RAG Triad Skoru',
      type: 'radar',
      data: [{
        value: [cr, ft, ar],
        name: 'RAG Triad Tri-Metric',
        areaStyle: { color: 'rgba(6, 182, 212, 0.4)' },
        lineStyle: { color: '#06b6d4', width: 2 },
        itemStyle: { color: '#38bdf8' }
      }]
    }]
  };

  triadChartInstance.setOption(option);
}

// --- Panel 3: Hardware Profiling & Ranking ---
function renderHardwareAndRankingChart(hw, ranking) {
  const chartDom = document.getElementById('hardwareProfilingChart');
  if (!hardwareChartInstance) {
    hardwareChartInstance = echarts.init(chartDom);
  }

  const option = {
    backgroundColor: '#060911',
    title: {
      text: `Top. Gecikme: ${hw.totalLatencyMs || hw.TotalLatencyMs} ms | GPU: ${hw.gpuRerankMs || hw.GpuRerankMs} ms | NDCG@3: ${ranking.ndcgAtK || ranking.NdcgAtK}`,
      textStyle: { color: '#f59e0b', fontSize: 11, fontFamily: 'Fira Code' },
      right: '3%',
      top: '4%'
    },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: '#0c1222',
      borderColor: '#f59e0b',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code' }
    },
    grid: { left: '15%', right: '8%', bottom: '12%', top: '20%', containLabel: true },
    xAxis: {
      type: 'value',
      name: 'Milisaniye (ms)',
      nameTextStyle: { color: '#64748b' },
      axisLabel: { color: '#94a3b8', fontFamily: 'Fira Code' },
      splitLine: { lineStyle: { color: 'rgba(255,255,255,0.05)' } }
    },
    yAxis: {
      type: 'category',
      // GPU adı API'den dinamik geliyor (hw.gpuDevice)
      data: ['Chunk & Ingest', 'BM25 Sparse', 'SIMD Dense', hw.gpuDevice || 'GPU Re-Rank', 'Triad Değlendirme'],
      axisLabel: { color: '#f8fafc', fontFamily: 'Plus Jakarta Sans', fontWeight: '600' }
    },
    series: [
      {
        name: 'Gecikme (ms)',
        type: 'bar',
        itemStyle: {
          color: function (params) {
            const colors = ['#6366f1', '#f59e0b', '#a855f7', '#10b981', '#06b6d4'];
            return colors[params.dataIndex];
          },
          borderRadius: [0, 4, 4, 0]
        },
        label: {
          show: true,
          position: 'right',
          formatter: '{c} ms',
          color: '#cbd5e1',
          fontFamily: 'Fira Code'
        },
        data: [
          hw.ingestMs || hw.IngestMs || 0.1,
          hw.bm25SearchMs || hw.Bm25SearchMs || 0.1,
          hw.simdDenseSearchMs || hw.SimdDenseSearchMs || 0.1,
          hw.gpuRerankMs || hw.GpuRerankMs || 0.1,
          hw.triadEvalMs || hw.TriadEvalMs || 0.1
        ]
      }
    ]
  };

  hardwareChartInstance.setOption(option);
}

// --- Panel 4: Sentence-by-Sentence Grounding Proof Explorer ---
function renderGroundingProofExplorer(sentenceDetails, overallFaithfulness) {
  const container = document.getElementById('groundingProofExplorer');
  if (!sentenceDetails || sentenceDetails.length === 0) {
    container.innerHTML = `<div style="text-align:center; color:#64748b; padding:2rem;">Aday yanıt girildiğinde cümle bazlı kanıtlar burada listelenir.</div>`;
    return;
  }

  let html = '';
  sentenceDetails.forEach(s => {
    const isGrounded = s.isGrounded !== undefined ? s.isGrounded : s.IsGrounded;
    const sentence = s.sentence || s.Sentence;
    const support = s.supportRatio || s.SupportRatio;
    const docId = s.bestMatchingDocId || s.BestMatchingDocId;
    const snippet = s.bestMatchingSnippet || s.BestMatchingSnippet;
    const idx = s.sentenceIndex || s.SentenceIndex;

    html += `
      <div class="proof-card ${isGrounded ? 'grounded' : 'ungrounded'}">
        <div class="proof-header">
          <span class="proof-title">Cümle ${idx}</span>
          <span class="proof-tag ${isGrounded ? 'green' : 'red'}">${isGrounded ? '✅ DOĞRULANDI' : '⚠️ HALÜSİNASYON'}</span>
        </div>
        <div class="proof-body">"${sentence}"</div>
        <div class="proof-meta">
          <span>Kanıt Desteği: <strong>${(support * 100).toFixed(0)}%</strong></span>
          ${docId ? `<span>Kaynak: <span class="source-tag">${docId}</span> (${snippet})</span>` : `<span>Kaynak: <span style="color:#f43f5e;">Eşleşen Doküman Yok</span></span>`}
        </div>
      </div>
    `;
  });

  container.innerHTML = html;
}

// ==========================================
// 💬 CLINICAL CHAT & RAG INSPECTOR ENGINE
// ==========================================

function handleChatKeyDown(e) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendChatMessage();
  }
}

function sendQuickPrompt(type) {
  const input = document.getElementById('chatInputText');
  if (type === 'penicillin') {
    input.value = "Penisilin alerjisi olan hastada Amoksisilin kullanılabilir mi? Alternatifi nedir?";
  } else if (type === 'paracetamol') {
    input.value = "Yetişkin bir hastada parasetamolün günlük maksimum dozu kaç mg'dır?";
  } else if (type === 'hallucination_test') {
    input.value = "Penisilin alerjisinde hastaya günde 2000mg Amoksisilin verilmesi güvenli midir?";
  }
  sendChatMessage();
}

async function sendChatMessage() {
  const input = document.getElementById('chatInputText');
  const query = input.value.trim();
  if (!query) return;

  const stream = document.getElementById('chatMessagesStream');

  // 1. Append User Message
  const userHtml = `
    <div class="chat-msg user-msg">
      <div class="msg-avatar">🧑‍⚕️</div>
      <div class="msg-bubble">
        <div class="msg-meta">Doktor / Klinik Kullanıcı</div>
        <div class="msg-content">${query}</div>
      </div>
    </div>
  `;
  stream.insertAdjacentHTML('beforeend', userHtml);
  input.value = '';
  stream.scrollTop = stream.scrollHeight;

  // 2. Append AI Pending Message
  const pendingId = `ai_msg_${Date.now()}`;
  const aiPendingHtml = `
    <div id="${pendingId}" class="chat-msg ai-msg">
      <div class="msg-avatar">🤖</div>
      <div class="msg-bubble">
        <div class="msg-meta">Klinik AI Asistanı • RAG & Guardrail Çalışıyor...</div>
        <div class="msg-content" style="color:#06b6d4;">⏳ Klinik dokümanlar taranıyor, GPU re-ranking ve dozaj kilidi denetleniyor...</div>
      </div>
    </div>
  `;
  stream.insertAdjacentHTML('beforeend', aiPendingHtml);
  stream.scrollTop = stream.scrollHeight;

  try {
    const corpusInput = document.getElementById('corpusInput');
    const corpusVal = corpusInput ? corpusInput.value.trim() : '';
    const docName = window.currentUploadedDocName || (corpusVal ? "Yuklenen_Dokuman.pdf" : "Klinik_Korpus.pdf");

    const res = await fetch(`${API_BASE}/api/chat/rag`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ 
        query: query,
        corpus: corpusVal,
        documentName: docName
      })
    });

    if (res.ok) {
      const data = await res.json();
      currentMessageTelemetry = data;
      renderAiResponseWithInlineTracing(pendingId, data);
      updateRagInspector(data);
    }
  } catch (err) {
    const aiBubble = document.getElementById(pendingId);
    if (aiBubble) {
      aiBubble.querySelector('.msg-content').innerHTML = `<span style="color:#f43f5e;">Bağlantı hatası: C# API'ye ulaşılamadı.</span>`;
    }
  }
}

function renderAiResponseWithInlineTracing(msgElemId, data) {
  const elem = document.getElementById(msgElemId);
  if (!elem) return;

  window.currentSentences = data.sentences || [];

  const bubble = elem.querySelector('.msg-bubble');
  const guardrailStatus = (data.telemetry && data.telemetry.guardrailStatus) || '';
  const isBlocked = guardrailStatus.includes('BLOCKED');

  let formattedSentencesHtml = '';
  let hasRealUngroundedFact = false;

  (data.sentences || []).forEach((s, idx) => {
    const isGrounded = s.isGrounded || false;
    const doc = (s.bestDoc || 'Bilinmiyor');
    const isMeta = doc.includes('Diyalog') || doc.includes('Meta');
    const ratio = Math.round((s.supportRatio || 0) * 100);

    if (!isGrounded && !isMeta) {
      hasRealUngroundedFact = true;
    }

    let badgeText = isMeta ? '💬 Sohbet' : (isGrounded ? '✅ ' + ratio + '%' : '⚠️ Uydurma');
    let cssClass = isMeta ? 'conversational' : (isGrounded ? 'grounded' : 'ungrounded');

    formattedSentencesHtml += `
      <span class="traced-sentence ${cssClass}"
            data-idx="${idx}"
            onmouseenter="onSentenceHoverByIndex(${idx})"
            onclick="onSentenceClickByIndex(${idx})"
            title="Tıkla: Kaynak PDF'te bu satırı incele">
        ${escapeHtml(s.sentence || '')}
        <small style="font-size:0.65rem; font-weight:bold; opacity:0.85;">[${badgeText}]</small>
      </span> 
    `;
  });

  const statusBadge = hasRealUngroundedFact
    ? '<span style="color:#f43f5e;">⚠️ GÜVENLİK KİLİDİ DEVREDE</span>'
    : '<span style="color:#10b981;">✅ KANITLANDI / DOĞRULANDI</span>';

  bubble.innerHTML = `
    <div class="msg-meta">
      Klinik AI Asistanı • ${statusBadge}
    </div>
    <div class="msg-content">
      ${formattedSentencesHtml || (data.answer || 'Yanıt alınamadı.')}
    </div>
  `;
}

function onSentenceHoverByIndex(idx) {
  if (!window.currentSentences || !window.currentSentences[idx]) return;
  const s = window.currentSentences[idx];
  const doc = s.bestDoc || 'Bilinmiyor';
  const isMeta = doc.includes('Diyalog') || doc.includes('Meta');
  const ratio = Math.round((s.supportRatio || 0) * 100);
  highlightSentenceProof(s.sentence || '', doc, s.snippet || '', s.isGrounded || false, ratio, isMeta);
}

function onSentenceClickByIndex(idx) {
  onSentenceHoverByIndex(idx);
  const s = window.currentSentences ? window.currentSentences[idx] : null;
  const doc = (s && s.bestDoc) || '';
  const isMeta = doc.includes('Diyalog') || doc.includes('Meta');
  if (!isMeta) {
    openSourceDocViewer();
  }
}

function updateRagInspector(data) {
  // Guard: telemetry yoksa işlem yapma
  if (!data || !data.telemetry) return;
  const t = data.telemetry;

  document.getElementById('activeMessageBadge').textContent = data.messageId || 'msg_live';

  // Güvenli sayı formatlaması - undefined/null crash önlenir
  const fmt2 = v => (typeof v === 'number' ? v.toFixed(2) : '---');
  const fmt1 = v => (typeof v === 'number' ? v.toFixed(1) : '---');
  const fmt0 = v => (typeof v === 'number' ? v.toFixed(0) : '---');

  // Metrics
  document.getElementById('inspConfidence').textContent = fmt2(t.retrievalConfidence);
  document.getElementById('inspRerankLift').textContent = `+${fmt1(t.rerankLiftPercent)}%`;
  document.getElementById('inspContextRel').textContent = `${fmt0(t.contextRelevancyPercent)}%`;
  document.getElementById('inspFaithfulness').textContent = `${fmt0(t.faithfulnessPercent)}%`;

  // Dosage Lock
  const dosageBox = document.getElementById('dosageLockBox');
  const dosageTitle = document.getElementById('dosageLockTitle');
  const dosageDesc = document.getElementById('dosageLockDesc');
  const dosageIcon = document.getElementById('dosageLockIcon');

  const dg = t.dosageGuard || {};
  if (dg.isValid !== false) {
    dosageBox.className = "dosage-lock-box";
    dosageIcon.textContent = "🛡️";
    dosageTitle.textContent = "Dozaj & Etken Madde Kilidi: ✅ DOĞRULANDI";
    dosageTitle.style.color = "#10b981";
    dosageDesc.textContent = dg.status || '';
  } else {
    dosageBox.className = "dosage-lock-box violation";
    dosageIcon.textContent = "🚨";
    dosageTitle.textContent = "Dozaj & Etken Madde Kilidi: ⚠️ İHLAL (ENGEL)";
    dosageTitle.style.color = "#f43f5e";
    dosageDesc.textContent = dg.status || '';
  }

  // Hardware Profiling - null guard eklendi
  const lms = t.latencyMs || {};
  document.getElementById('inspTotalTime').textContent = `${lms.total ?? '---'} ms`;
  document.getElementById('inspBm25Ms').textContent = `${lms.bm25 ?? '---'} ms`;
  document.getElementById('inspSimdMs').textContent = `${lms.simdVector ?? '---'} ms`;
  document.getElementById('inspGpuMs').textContent = `${lms.gpuRerank ?? '---'} ms`;

  // Decision & Execution Trace Pipeline Steps
  const stepsContainer = document.getElementById('decisionPipelineSteps');
  if (stepsContainer) {
    const totalSentences = (data.sentences || []).length;
    const groundedCount = (data.sentences || []).filter(s => s.isGrounded || (s.bestDoc && (s.bestDoc.includes('Diyalog') || s.bestDoc.includes('Meta')))).length;
    const ungroundedCount = totalSentences - groundedCount;
    const topDoc = (t.retrievedChunks && t.retrievedChunks[0]) ? t.retrievedChunks[0].doc : 'Klinik Havuz';
    const topScore = (t.retrievedChunks && t.retrievedChunks[0]) ? (t.retrievedChunks[0].score || 0).toFixed(2) : '0.00';
    const dgValid = (t.dosageGuard && t.dosageGuard.isValid !== false);

    stepsContainer.innerHTML = `
      <div class="step-item">
        <span class="step-num">1</span>
        <div class="step-txt">
          <strong>Soru Vektörizasyonu & SIMD Embedding</strong> (${lms.simdVector ?? 0} ms)
        </div>
        <span class="step-badge green">CPU AVX2</span>
      </div>
      <div class="step-item">
        <span class="step-num">2</span>
        <div class="step-txt">
          <strong>Hibrit Arama</strong> (BM25: ${lms.bm25 ?? 0} ms) + RRF Sıralama
        </div>
        <span class="step-badge cyan">K=60 Fusion</span>
      </div>
      <div class="step-item">
        <span class="step-num">3</span>
        <div class="step-txt">
          <strong>RTX 4060 Ti GPU Re-Ranking</strong> ➔ En İyi: <em>${topDoc}</em> (Skor: ${topScore})
        </div>
        <span class="step-badge yellow">${lms.gpuRerank ?? 0} ms GPU</span>
      </div>
      <div class="step-item">
        <span class="step-num">4</span>
        <div class="step-txt">
          <strong>Ollama ${t.llmModel || 'qwen2.5:7b'}</strong> Klinik Üretim (${totalSentences} Cümle)
        </div>
        <span class="step-badge purple">${lms.llmGenerate ?? 0} ms</span>
      </div>
      <div class="step-item">
        <span class="step-num">5</span>
        <div class="step-txt">
          <strong>Olgusal Grounding</strong> (${groundedCount}/${totalSentences} Doğrulandı) & Dozaj Kilidi
        </div>
        <span class="step-badge ${dgValid && ungroundedCount === 0 ? 'green' : 'red'}">${dgValid && ungroundedCount === 0 ? 'ONAYLANDI' : 'UYARI / KİLİT'}</span>
      </div>
    `;
  }

  // Raw JSON
  document.getElementById('rawTelemetryJson').textContent = JSON.stringify(data, null, 2);

  // Default first sentence proof
  if (data.sentences && data.sentences.length > 0) {
    const s0 = data.sentences[0];
    const doc0 = s0.bestDoc || 'Bilinmiyor';
    const isMeta0 = doc0.includes('Diyalog') || doc0.includes('Meta');
    highlightSentenceProof(
      s0.sentence || '',
      doc0,
      s0.snippet || '',
      s0.isGrounded || false,
      Math.round((s0.supportRatio || 0) * 100),
      isMeta0
    );
  }
}

function findMatchingLineInCorpus(snippet, sentence, fullCorpus) {
  if (!fullCorpus) return -1;
  const lines = fullCorpus.split('\n');
  
  // 1. Try snippet direct search
  if (snippet) {
    const cleanSnippet = snippet.replace(/^[0-9]\.\s*/, '').trim().toLowerCase();
    if (cleanSnippet.length > 6) {
      const targetSnippet = cleanSnippet.slice(0, 30);
      const idx = lines.findIndex(l => l.toLowerCase().includes(targetSnippet));
      if (idx >= 0) return idx;
    }
  }

  // 2. Try sentence keywords match
  const cleanSentence = (sentence || '').toLowerCase().replace(/[.,\/#!$%\^&\*;:{}=\-_`~()?"'0-9]/g, ' ');
  const words = cleanSentence.split(/\s+/).filter(w => w.length >= 4 && !['icin', 'veya', 'olan', 'gibi', 'kadar', 'daha', 'olarak', 'hangi', 'tedavisinde', 'kullanilir'].includes(w));
  
  if (words.length > 0) {
    let bestScore = 0;
    let bestIdx = -1;

    for (let i = 0; i < lines.length; i++) {
      const lineLower = lines[i].toLowerCase();
      let matchCount = 0;
      for (const w of words) {
        if (lineLower.includes(w)) matchCount++;
      }
      if (matchCount > bestScore && matchCount >= 2) {
        bestScore = matchCount;
        bestIdx = i;
      }
    }
    if (bestIdx >= 0) return bestIdx;
  }

  return -1;
}

function highlightSentenceProof(sentence, doc, snippet, isGrounded, ratio, isMeta = false) {
  const container = document.getElementById('activeSentenceProofContent');
  container.className = "proof-content-box highlight-active";

  const corpusInput = document.getElementById('corpusInput');
  let fullCorpus = (corpusInput && corpusInput.value.trim()) ? corpusInput.value.trim() : (window.currentUploadedCorpus || '');
  
  let matchedLineIndex = -1;
  if (fullCorpus && !isMeta) {
    matchedLineIndex = findMatchingLineInCorpus(snippet, sentence, fullCorpus);
  }

  window.activeTargetLineNumber = matchedLineIndex >= 0 ? (matchedLineIndex + 1) : null;
  window.activeTargetDoc = doc;
  window.activeTargetSnippet = snippet;
  window.activeTargetSentence = sentence;

  const lineIndicator = document.getElementById('activeLineIndicator');
  const openDocBtn = document.getElementById('openDocViewerBtn');

  if (matchedLineIndex >= 0 && !isMeta) {
    if (lineIndicator) lineIndicator.textContent = `📍 Satır #${matchedLineIndex + 1}`;
    if (openDocBtn) {
      openDocBtn.style.display = 'flex';
      openDocBtn.innerHTML = `📖 PDF'te Satır #${matchedLineIndex + 1}'e Git & İncele`;
    }
  } else if (isMeta) {
    if (lineIndicator) lineIndicator.textContent = `💬 Diyalog`;
    if (openDocBtn) openDocBtn.style.display = 'none';
  } else {
    if (lineIndicator) lineIndicator.textContent = ``;
    if (openDocBtn) openDocBtn.style.display = 'none';
  }

  let headerColor = isMeta ? '#38bdf8' : (isGrounded ? '#10b981' : '#f43f5e');
  let headerText = isMeta 
    ? '💬 Klinik Diyalog / Nezaket Yanıtı' 
    : (isGrounded ? `✅ Olgusal Desteklenen Cümle (${ratio}% Örtüşme)` : `⚠️ Kaynaksız / Halüsinasyon Cümlesi (${ratio}% Örtüşme)`);

  let lineBadgeHtml = (matchedLineIndex >= 0 && !isMeta) 
    ? `<span style="background:rgba(245,158,11,0.2); color:#f59e0b; padding:0.1rem 0.4rem; border-radius:4px; font-family:var(--font-mono); font-size:0.65rem; margin-left:0.4rem;">Satır #${matchedLineIndex + 1}</span>` 
    : '';

  container.innerHTML = `
    <div style="margin-bottom:0.4rem; color:${headerColor}; font-weight:bold; display:flex; align-items:center;">
      <span>${headerText}</span>
      ${lineBadgeHtml}
    </div>
    <div style="font-style:italic; margin-bottom:0.6rem; color:#f8fafc; cursor:pointer;" onclick="openSourceDocViewer()" title="Tıkla: Doküman modalını aç">"${sentence}"</div>
    <div style="border-top:1px solid rgba(255,255,255,0.06); padding-top:0.4rem; font-size:0.7rem;">
      <span style="color:#64748b;">Eşleşen Kaynak PDF:</span> <strong style="color:#06b6d4;">${doc}</strong><br/>
      <span style="color:#64748b;">Doküman Paragrafı:</span> <span style="color:#cbd5e1;">"${snippet}"</span>
    </div>
  `;
}

// --- Source Document Line Viewer Modal ---
function escapeRegex(string) {
  return string.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function openSourceDocViewer() {
  try {
    const corpusInput = document.getElementById('corpusInput');
    let fullCorpus = (corpusInput && corpusInput.value.trim()) ? corpusInput.value.trim() : (window.currentUploadedCorpus || '');
    
    const container = document.getElementById('sourceDocLinesContainer');
    const modal = document.getElementById('sourceDocModal');
    const targetBadge = document.getElementById('docViewerTargetBadge');
    const modalTitle = document.getElementById('sourceDocModalTitle');

    if (!fullCorpus) {
      fullCorpus = "Doküman içeriği henüz yüklenmedi.";
    }

    const docName = window.activeTargetDoc || window.currentUploadedDocName || 'Dokuman.pdf';
    modalTitle.textContent = `📖 Kaynak Doküman: ${docName}`;
    const targetLine = window.activeTargetLineNumber || 1;
    targetBadge.textContent = `📍 Hedef: Satır #${targetLine}`;

    const lines = fullCorpus.split('\n');
    let html = '';

    lines.forEach((line, idx) => {
      const lineNum = idx + 1;
      const isTarget = (lineNum === targetLine);
      const targetClass = isTarget ? 'doc-line active-highlight-line' : 'doc-line';
      const targetId = `doc_line_${lineNum}`;
      
      let lineContentHtml = escapeHtml(line || ' ');
      if (isTarget && window.activeTargetSentence) {
        try {
          const kw = window.activeTargetSentence.split(/\s+/).filter(w => w.length > 5);
          kw.forEach(w => {
            const regex = new RegExp(`(${escapeRegex(w)})`, 'gi');
            lineContentHtml = lineContentHtml.replace(regex, '<mark style="background:#f59e0b; color:#000; padding:0 2px; border-radius:2px;">$1</mark>');
          });
        } catch (e) {}
      }

      html += `
        <div id="${targetId}" class="${targetClass}">
          <div class="doc-line-num">${lineNum}</div>
          <div class="doc-line-text">${lineContentHtml}</div>
        </div>
      `;
    });

    container.innerHTML = html;
    modal.style.display = 'flex';

    // Smooth scroll to target line
    setTimeout(() => {
      const targetEl = document.getElementById(`doc_line_${targetLine}`);
      if (targetEl) {
        targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
    }, 100);
  } catch (err) {
    console.error("Doc viewer open error:", err);
  }
}

function closeSourceDocModal() {
  const modal = document.getElementById('sourceDocModal');
  if (modal) modal.style.display = 'none';
}

// --- Stres Testi Modal Koşturucu ---
function openBenchmarkModal() {
  document.getElementById('benchmarkModal').style.display = 'flex';
}

function closeBenchmarkModal() {
  document.getElementById('benchmarkModal').style.display = 'none';
}

async function runLiveBenchmark() {
  const btn = document.getElementById('runBenchmarkBtn');
  const tbody = document.getElementById('benchmarkCasesTbody');
  
  btn.disabled = true;
  btn.textContent = "⏳ Koşturuluyor (Needle in a Haystack & GPU)...";
  tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; padding: 2rem;"><span style="color:#06b6d4;">⚙️ 10 Çöp Doküman Arasına Gizlenmiş Tıbbi İğne Test Ediliyor...</span></td></tr>`;

  try {
    const res = await fetch(`${API_BASE}/api/lab/stress-test`, { method: 'POST' });
    if (res.ok) {
      const data = await res.json();
      
      document.getElementById('bmPassRate').textContent = `${data.passRate}% (${data.passedTests}/${data.totalTests})`;
      document.getElementById('bmFaithfulness').textContent = `${data.avgFaithfulness}%`;
      document.getElementById('bmTotalTime').textContent = `${data.totalLatencyMs} ms`;
      document.getElementById('bmGpuSpeed').textContent = data.gpuBenchmark.isGpuActive ? `${data.gpuBenchmark.latencyMs} ms` : "CPU";

      let rowsHtml = '';
      data.testCases.forEach(tc => {
        rowsHtml += `
          <tr>
            <td class="tc-id">${tc.id || tc.Id}</td>
            <td><strong>${tc.name || tc.Name}</strong><br/><span style="color:#64748b; font-size:0.7rem;">Durum: ${tc.finalState || tc.FinalState}</span></td>
            <td style="max-width:260px; font-family:var(--font-mono); font-size:0.7rem;">${tc.query || tc.Query}</td>
            <td style="font-family:var(--font-mono); font-weight:bold; color:${(tc.faithfulness || tc.Faithfulness) > 0.7 ? '#10b981' : '#f43f5e'}">${((tc.faithfulness || tc.Faithfulness)*100).toFixed(0)}%</td>
            <td style="font-family:var(--font-mono);">${tc.latencyMs || tc.LatencyMs} ms</td>
            <td><span class="verdict-tag ${tc.passed || tc.Passed ? 'pass' : 'fail'}">${tc.verdict || tc.Verdict}</span></td>
          </tr>
        `;
      });

      if (data.gpuBenchmark && data.gpuBenchmark.isGpuActive) {
        const gpuDevice = data.gpuBenchmark.device || 'GPU (DirectML)';
        const modelName = 'ONNX ms-marco-MiniLM-L-6-v2'; // model adı sabit — benchmark'e özeldir
        rowsHtml += `
          <tr style="background: rgba(245, 158, 11, 0.08);">
            <td class="tc-id">GPU-01</td>
            <td><strong>${gpuDevice} Re-Ranking (Needle Match)</strong><br/><span style="color:#f59e0b; font-size:0.7rem;">${modelName}</span></td>
            <td style="max-width:260px; font-family:var(--font-mono); font-size:0.7rem;">${data.gpuBenchmark.topChunk || ''}</td>
            <td style="font-family:var(--font-mono); font-weight:bold; color:#10b981;">Skor: ${(data.gpuBenchmark.topScore*100).toFixed(1)}%</td>
            <td style="font-family:var(--font-mono); font-weight:bold; color:#f59e0b;">${data.gpuBenchmark.latencyMs} ms</td>
            <td><span class="verdict-tag pass">🚀 GPU ZİRVE</span></td>
          </tr>
        `;
      }

      tbody.innerHTML = rowsHtml;
    }
  } catch (err) {
    tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color:#f43f5e; padding: 2rem;">API Bağlantı Hatası: ${err.message}</td></tr>`;
  } finally {
    btn.disabled = false;
    btn.textContent = "⚡ TESTLERİ KOŞTUR";
  }
}

// 8. Multi-format Document Ingestion (PDF, TXT, MD, JSON, CSV) & Drag-and-Drop
let selectedUploadFile = null;

function handleFileSelected(event) {
  const file = event.target.files[0];
  if (!file) return;
  setupFileForUpload(file);
}

function setupFileForUpload(file) {
  selectedUploadFile = file;
  const label = document.getElementById('uploadFileNameLabel');
  const btn = document.getElementById('uploadProcessBtn');
  const pwGroup = document.getElementById('pdfPasswordGroup');
  const status = document.getElementById('uploadStatusBadge');

  const sizeKb = (file.size / 1024).toFixed(1);
  if (label) label.innerHTML = `📄 <strong>${file.name}</strong> (${sizeKb} KB)`;
  if (btn) {
    btn.style.display = 'block';
    btn.textContent = `⚡ "${file.name}" Dosyasını Çıkar & Ekle`;
  }
  if (status) status.style.display = 'none';

  // PDF ise parola alanını göster
  if (file.name.toLowerCase().endsWith('.pdf')) {
    if (pwGroup) pwGroup.style.display = 'block';
  } else {
    if (pwGroup) pwGroup.style.display = 'none';
  }

  // Kullanıcıyı bekletmeden hemen otomatik yükle ve ayrıştır
  uploadAndIngestDocument();
}

async function uploadAndIngestDocument() {
  if (!selectedUploadFile) return;

  const btn = document.getElementById('uploadProcessBtn');
  const status = document.getElementById('uploadStatusBadge');
  const pwInput = document.getElementById('pdfPasswordInput');
  const password = pwInput ? pwInput.value.trim() : '';

  btn.disabled = true;
  btn.textContent = '⏳ Doküman Ayrıştırılıyor...';
  status.style.display = 'block';
  status.className = 'upload-status loading';
  status.textContent = 'Dosya sunucuya gönderiliyor ve metin katmanları çıkarılıyor...';

  try {
    const formData = new FormData();
    formData.append('file', selectedUploadFile);
    if (password) {
      formData.append('password', password);
    }

    const res = await fetch(`${API_BASE}/api/documents/upload`, {
      method: 'POST',
      body: formData
    });

    const data = await res.json();

    if (!res.ok) {
      throw new Error(data.error || 'Dosya işlenirken hata oluştu.');
    }

    // Korpus alanına çıkarılan metni yerleştir
    const corpusInput = document.getElementById('corpusInput');
    if (corpusInput && data.combinedText) {
      corpusInput.value = data.combinedText;
    }

    window.currentUploadedCorpus = data.combinedText || '';
    window.currentUploadedDocName = data.fileName || 'Dokuman.pdf';
    window.currentUploadedChunks = data.chunks || [];

    status.className = 'upload-status success';
    status.innerHTML = `✅ <strong>${data.fileName}</strong> başarıyla aktarıldı!<br/>` +
                       `📄 ${data.totalPagesOrDocs} Bölüm/Sayfa • 🧩 ${data.totalChunks} Chunk • 📝 ${data.totalCharacters} Karakter`;

    btn.textContent = '✅ Korpus Güncellendi';

    const viewChunksBtn = document.getElementById('viewChunksBtn');
    if (viewChunksBtn && data.chunks && data.chunks.length > 0) {
      viewChunksBtn.style.display = 'block';
      viewChunksBtn.textContent = `📑 Çıkarılan ${data.chunks.length} Chunk'ı İncele`;
    }

    const chatBadge = document.getElementById('chatActiveDocBadge');
    if (chatBadge && data.fileName) {
      chatBadge.innerHTML = `📄 <strong>${data.fileName}</strong> (${data.totalChunks} Chunk Aktif)`;
    }

    // PDF içeriği Parol / Parasetamol ise veya yeni dosya ise sorguyu ve aday yanıtı dokümanla uyumlu yap
    const queryInput = document.getElementById('queryInput');
    const candidateInput = document.getElementById('candidateResponseInput');
    const combinedLower = (data.combinedText || '').toLowerCase();

    if (combinedLower.includes('parol') || combinedLower.includes('parasetamol')) {
      if (queryInput) queryInput.value = "Parol tablet ne için kullanılır ve yetişkin dozu nedir?";
      if (candidateInput) candidateInput.value = "Parol hafif ve orta şiddetli ağrılarda kullanılır. Yetişkinlerde 6 saatte bir 500mg-1000mg aralığında alınabilir.";
    } else if (combinedLower.includes('amoksisilin') || combinedLower.includes('penisilin')) {
      if (queryInput) queryInput.value = "Penisilin alerjisinde hangi antibiyotik alternatiftir?";
      if (candidateInput) candidateInput.value = "Şiddetli penisilin alerjisi olan hastalarda alternatif olarak makrolid grubu antibiyotikler güvenle tercih edilebilir.";
    }

    // RAG Pipeline'ını yeni korpusla anında çalıştır
    await executeFullPipeline();

  } catch (err) {
    status.className = 'upload-status error';
    status.innerHTML = `❌ Hata: ${err.message}`;
    btn.textContent = '⚠️ Yeniden Dene';
  } finally {
    btn.disabled = false;
  }
}

// --- Chunk Explorer Modal ---
function openChunksModal() {
  const modal = document.getElementById('chunksModal');
  const tbody = document.getElementById('chunksTableTbody');
  const sub = document.getElementById('chunksModalSub');
  const chunks = window.currentUploadedChunks || [];

  if (!modal || !tbody) return;

  modal.style.display = 'flex';
  sub.textContent = `Toplam ${chunks.length} ayrıştırılmış chunk parçası ve metadata istatistiği`;

  if (chunks.length === 0) {
    tbody.innerHTML = `<tr><td colspan="4" style="text-align:center; padding: 2rem; color:#64748b;">Henüz ayrıştırılmış chunk bulunamadı.</td></tr>`;
    return;
  }

  let html = '';
  chunks.forEach((c, i) => {
    const docId = c.documentId || c.DocumentId || `Sayfa_${i + 1}`;
    const len = c.length || c.Length || (c.content ? c.content.length : 0);
    const content = (c.content || c.Content || '').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    html += `
      <tr>
        <td class="tc-id" style="width:40px;">${i + 1}</td>
        <td style="width:130px; font-weight:bold; color:#38bdf8;">${docId}</td>
        <td style="width:100px; font-family:var(--font-mono); font-size:0.75rem; color:#a855f7;">${len} karakter</td>
        <td style="font-family:var(--font-mono); font-size:0.72rem; line-height:1.4; color:#cbd5e1; max-width:550px;">
          ${content}
        </td>
      </tr>
    `;
  });
  tbody.innerHTML = html;
}

function closeChunksModal() {
  const modal = document.getElementById('chunksModal');
  if (modal) modal.style.display = 'none';
}

function setupDragAndDrop() {
  const dropzone = document.getElementById('uploadDropzone');
  if (!dropzone) return;

  ['dragenter', 'dragover'].forEach(eventName => {
    dropzone.addEventListener(eventName, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropzone.classList.add('drag-over');
    }, false);
  });

  ['dragleave', 'drop'].forEach(eventName => {
    dropzone.addEventListener(eventName, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropzone.classList.remove('drag-over');
    }, false);
  });

  dropzone.addEventListener('drop', (e) => {
    const dt = e.dataTransfer;
    const files = dt.files;
    if (files && files.length > 0) {
      setupFileForUpload(files[0]);
    }
  }, false);
}

// Window Resize
window.addEventListener('resize', () => {
  if (vectorChartInstance) vectorChartInstance.resize();
  if (triadChartInstance) triadChartInstance.resize();
  if (hardwareChartInstance) hardwareChartInstance.resize();
});

// Global Keyboard Shortcuts (ESC to close modals or exit fullscreen)
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    closeSourceDocModal();
    closeChunksModal();
    closeBenchmarkModal();
    document.querySelectorAll('.viz-box.fullscreen-viz').forEach(el => el.classList.remove('fullscreen-viz'));
  }
});

// Init
document.addEventListener('DOMContentLoaded', async () => {
  setupDragAndDrop();
  await checkApiHealth();
  await executeFullPipeline();
});
