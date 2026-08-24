// TrustLab Scientific RAG Workbench & Clinical Observability Assistant
// Integrated with C# .NET 10 API, RTX 4060 Ti GPU & Grounding Guardrails

let vectorChartInstance = null;
let triadChartInstance = null;
let hardwareChartInstance = null;

const API_BASE = "http://localhost:5000";
let activeView = "lab"; // 'lab' or 'chat'
let currentMessageTelemetry = null;

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

// --- Panel 1: 2D Vector Space (PCA + L2 Distance) ---
function render2DVectorSpace(query, queryVector, docVectors) {
  const chartDom = document.getElementById('vectorSpaceChart');
  if (!vectorChartInstance) {
    vectorChartInstance = echarts.init(chartDom);
  }

  function project2D(vec) {
    if (!vec || vec.length === 0) return [0, 0];
    let x = 0, y = 0;
    for (let i = 0; i < vec.length; i++) {
      x += vec[i] * Math.cos((i * 2 * Math.PI) / vec.length);
      y += vec[i] * Math.sin((i * 2 * Math.PI) / vec.length);
    }
    return [parseFloat(x.toFixed(3)), parseFloat(y.toFixed(3))];
  }

  const queryPoint = project2D(queryVector);
  const docPoints = (docVectors || []).map((dv, idx) => {
    const p = project2D(dv.vector);
    return {
      name: dv.documentId || `Doc ${idx + 1}`,
      value: [p[0], p[1], dv.cosineDistance, dv.euclideanDistance],
      content: dv.content
    };
  });

  const linesData = docPoints.map(dp => ({
    coords: [queryPoint, [dp.value[0], dp.value[1]]],
    lineStyle: {
      color: dp.value[2] < 0.4 ? '#10b981' : dp.value[2] < 0.7 ? '#6366f1' : '#64748b',
      width: Math.max(1, (1.0 - dp.value[2]) * 4),
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
            return `<strong>🔍 CANLI SORGU (QUERY)</strong><br/>Koordinat: [${params.data.value[0]}, ${params.data.value[1]}]`;
          }
          return `<strong>📄 ${params.data.name}</strong><br/>
                  Cosine Mesafe: <span style="color:#10b981;font-weight:bold;">${params.data.value[2]}</span><br/>
                  Euclidean (L2): <span style="color:#06b6d4;font-weight:bold;">${params.data.value[3]}</span><br/>
                  <span style="color:#94a3b8; font-size:10px;">${(params.data.content || '').slice(0, 60)}...</span>`;
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
        symbolSize: function (val) { return 14 + ((1.0 - val[2]) * 16); },
        itemStyle: {
          color: function (params) {
            const cosDist = params.data.value[2];
            return cosDist < 0.4 ? '#10b981' : cosDist < 0.7 ? '#6366f1' : '#f43f5e';
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
    const res = await fetch(`${API_BASE}/api/chat/rag`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: query })
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

  const bubble = elem.querySelector('.msg-bubble');
  // Güvenli guard: telemetry veya guardrailStatus yoksa crash önlenir
  const guardrailStatus = (data.telemetry && data.telemetry.guardrailStatus) || '';
  const isBlocked = guardrailStatus.includes('BLOCKED');

  let formattedSentencesHtml = '';
  (data.sentences || []).forEach(s => {
    const isGrounded = s.isGrounded || false;
    const sText = (s.sentence || '').replace(/'/g, "\\'").replace(/"/g, '&quot;');
    const sTextDisplay = s.sentence || '';
    const doc = (s.bestDoc || 'Bilinmiyor');
    const snippet = (s.snippet || '').replace(/'/g, "\\'").replace(/"/g, '&quot;');
    const ratio = Math.round((s.supportRatio || 0) * 100);

    formattedSentencesHtml += `
      <span class="traced-sentence ${isGrounded ? 'grounded' : 'ungrounded'}"
            onmouseenter="highlightSentenceProof('${sText}', '${doc}', '${snippet}', ${isGrounded}, ${ratio})"
            onclick="highlightSentenceProof('${sText}', '${doc}', '${snippet}', ${isGrounded}, ${ratio})">
        ${sTextDisplay}
        <small style="font-size:0.65rem; font-weight:bold; opacity:0.85;">[${isGrounded ? '✅ ' + ratio + '%' : '⚠️ Uydurma'}]</small>
      </span> 
    `;
  });

  bubble.innerHTML = `
    <div class="msg-meta">
      Klinik AI Asistanı • ${isBlocked ? '<span style="color:#f43f5e;">⚠️ GÜVENLİK KİLİDİ DEVREDE</span>' : '<span style="color:#10b981;">✅ KANITLANDI</span>'}
    </div>
    <div class="msg-content">
      ${formattedSentencesHtml || (data.answer || 'Yanıt alınamadı.')}
    </div>
  `;
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

  // Raw JSON
  document.getElementById('rawTelemetryJson').textContent = JSON.stringify(data, null, 2);

  // Default first sentence proof
  if (data.sentences && data.sentences.length > 0) {
    const s0 = data.sentences[0];
    highlightSentenceProof(
      s0.sentence || '',
      s0.bestDoc || 'Bilinmiyor',
      s0.snippet || '',
      s0.isGrounded || false,
      Math.round((s0.supportRatio || 0) * 100)
    );
  }
}

function highlightSentenceProof(sentence, doc, snippet, isGrounded, ratio) {
  const container = document.getElementById('activeSentenceProofContent');
  container.className = "proof-content-box highlight-active";

  container.innerHTML = `
    <div style="margin-bottom:0.4rem; color:${isGrounded ? '#10b981' : '#f43f5e'}; font-weight:bold;">
      ${isGrounded ? '✅ Olgusal Desteklenen Cümle' : '⚠️ Kaynaksız / Halüsinasyon Cümlesi'} (${ratio}% Örtüşme)
    </div>
    <div style="font-style:italic; margin-bottom:0.6rem; color:#f8fafc;">"${sentence}"</div>
    <div style="border-top:1px solid rgba(255,255,255,0.06); padding-top:0.4rem; font-size:0.7rem;">
      <span style="color:#64748b;">Eşleşen Kaynak PDF:</span> <strong style="color:#06b6d4;">${doc}</strong><br/>
      <span style="color:#64748b;">Doküman Paragrafı:</span> <span style="color:#cbd5e1;">"${snippet}"</span>
    </div>
  `;
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
  label.innerHTML = `📄 <strong>${file.name}</strong> (${sizeKb} KB)`;
  btn.style.display = 'block';
  btn.textContent = `⚡ "${file.name}" Dosyasını Çıkar & Ekle`;
  status.style.display = 'none';

  // PDF ise parola alanını göster
  if (file.name.toLowerCase().endsWith('.pdf')) {
    pwGroup.style.display = 'block';
  } else {
    pwGroup.style.display = 'none';
  }
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

    status.className = 'upload-status success';
    status.innerHTML = `✅ <strong>${data.fileName}</strong> başarıyla aktarıldı!<br/>` +
                       `📄 ${data.totalPagesOrDocs} Bölüm/Sayfa • 🧩 ${data.totalChunks} Chunk • 📝 ${data.totalCharacters} Karakter`;

    btn.textContent = '✅ Korpus Güncellendi';

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

// Init
document.addEventListener('DOMContentLoaded', async () => {
  setupDragAndDrop();
  await checkApiHealth();
  await executeFullPipeline();
});
