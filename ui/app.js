/**
 * TrustLab — Deterministic RAG & Guardrail Engine UI Controller
 * High-performance scientific workbench & clinical observability assistant
 */

// ==========================================================================
// 1. STATE & CONSTANTS
// ==========================================================================
const API_BASE = "http://localhost:5000";

const state = {
  activeView: 'lab', // default to lab/scientific view or chat
  currentSentences: [],
  currentTelemetry: null,
  activeTargetDoc: null,
  activeTargetSnippet: null,
  activeTargetSentence: null,
  activeTargetLineNumber: null,
  uploadedDocName: null,
  uploadedCorpus: null,
  uploadedChunks: []
};

// Chart instances
let vectorChart = null;
let triadChart = null;
let hardwareChart = null;

// Presets for scientific evaluation
const PRESETS = {
  hybrid_match: {
    query: "Penisilin alerjisinde hangi antibiyotik kontrendikedir?",
    corpus: `Doküman 1: Şiddetli penisilin anafilaksi öyküsü olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir.\nDoküman 2: Alternatif olarak makrolid grubu antibiyotikler (Klaritromisin, Azitromisin) güvenle tercih edilebilir.\nDoküman 3: İtalyan mutfağında spagetti yaparken tenceredeki su kaynadıktan sonra tuz atılmalıdır.`,
    candidate: "Şiddetli penisilin alerjisi olan hastalarda Amoksisilin kullanımı mutlak kontrendikedir. Alternatif olarak makrolid grubu antibiyotikler güvenle tercih edilebilir."
  },
  exact_keyword: {
    query: "CS0234 namespace TrustLab Rag eksik assembly hatası",
    corpus: `Doküman 1: Derleyici hatası CS0234: The type or namespace name 'Rag' does not exist in the namespace 'TrustLab' assembly referansı eklenerek çözülür.\nDoküman 2: C# projelerinde Clean Architecture katmanları arasındaki bağımlılıklar csproj referanslarıyla kurulur.\nDoküman 3: BM25 algoritması nadir anahtar kelimeleri ve hata kodlarını yüksek IDF değeri ile ödüllendirir.`,
    candidate: "CS0234 hatası TrustLab Rag projesine assembly referansı eklenerek çözülür."
  },
  hallucination_trap: {
    query: "TrustLab kullanıcı şifrelerini hangi bulut veritabanında saklar?",
    corpus: `Doküman 1: TrustLab mimarisi tamamen yerel disk tabanlı in-memory vektör indeksleri ve BM25 depoları kullanır.\nDoküman 2: Güvenilirlik testleri için ExecutionTracer sınıfı milisaniye bazında gecikme ve token denetimi yapar.\nDoküman 3: Deterministik devre kesici, desteksiz iddialarda doğrudan güvenli fallback yanıtı üretir.`,
    candidate: "TrustLab kullanıcı şifrelerini AWS DynamoDB ve Redis veritabanında 256-bit AES ile şifreleyerek bulutta saklar."
  }
};

// ==========================================================================
// 2. UTILITY FUNCTIONS
// ==========================================================================
function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function escapeRegex(str) {
  if (!str) return '';
  return String(str).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function formatNumber(num, decimals = 0) {
  if (typeof num !== 'number' || isNaN(num)) return '---';
  return num.toFixed(decimals);
}

function autoResizeTextarea(textarea) {
  textarea.style.height = 'auto';
  textarea.style.height = Math.min(textarea.scrollHeight, 120) + 'px';
}

// ==========================================================================
// 3. API CLIENT
// ==========================================================================
const api = {
  async getStatus() {
    const res = await fetch(`${API_BASE}/api/system/status`, { signal: AbortSignal.timeout(4000) });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async evaluateLab(payload) {
    const res = await fetch(`${API_BASE}/api/lab/evaluate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async sendChat(query, corpus, documentName) {
    const res = await fetch(`${API_BASE}/api/chat/rag`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        query: query,
        corpus: corpus || undefined,
        documentName: documentName || undefined
      })
    });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async runStressTest() {
    const res = await fetch(`${API_BASE}/api/lab/stress-test`, { method: 'POST' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async getDocuments() {
    const res = await fetch(`${API_BASE}/api/documents/list`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async deleteDocument(id) {
    const res = await fetch(`${API_BASE}/api/documents/${encodeURIComponent(id)}`, { method: 'DELETE' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async clearDocuments() {
    const res = await fetch(`${API_BASE}/api/documents/clear`, { method: 'DELETE' });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
  },

  async uploadDocuments(files, password) {
    const formData = new FormData();
    for (let i = 0; i < files.length; i++) {
      formData.append('files', files[i]);
    }
    if (password) formData.append('password', password);

    const res = await fetch(`${API_BASE}/api/documents/upload`, {
      method: 'POST',
      body: formData
    });
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Dosya yükleme başarısız');
    return data;
  }
};

// ==========================================================================
// 4. VIEW & NAVIGATION CONTROLLERS
// ==========================================================================
async function checkApiHealth() {
  const badge = document.getElementById('apiStatusBadge');
  const text = document.getElementById('apiStatusText');
  try {
    const data = await api.getStatus();
    badge.className = "status-indicator";
    const gpu = data.gpuDevice ? data.gpuDevice.replace(' (DirectML)', '') : 'CPU';
    text.textContent = `API Aktif • ${gpu}`;
    return true;
  } catch (err) {
    badge.className = "status-indicator offline";
    text.textContent = "API Çevrimdışı";
    return false;
  }
}

function switchViewMode(mode) {
  state.activeView = mode;
  const labTab = document.getElementById('tabLabMode');
  const chatTab = document.getElementById('tabChatMode');
  const labContainer = document.getElementById('labViewContainer');
  const chatContainer = document.getElementById('chatViewContainer');

  if (mode === 'chat') {
    chatTab.classList.add('active');
    labTab.classList.remove('active');
    chatContainer.style.display = 'grid';
    labContainer.style.display = 'none';
  } else {
    labTab.classList.add('active');
    chatTab.classList.remove('active');
    chatContainer.style.display = 'none';
    labContainer.style.display = 'grid';
    setTimeout(resizeAllCharts, 120);
  }
}

function togglePresetDropdown() {
  const dropdown = document.getElementById('presetDropdown');
  dropdown.classList.toggle('show');
}

// Close dropdown on click outside
document.addEventListener('click', (e) => {
  const wrapper = document.querySelector('.dropdown-wrapper');
  const dropdown = document.getElementById('presetDropdown');
  if (wrapper && !wrapper.contains(e.target) && dropdown) {
    dropdown.classList.remove('show');
  }
});

function loadExperimentPreset(presetKey) {
  const p = PRESETS[presetKey];
  if (!p) return;

  const dropdown = document.getElementById('presetDropdown');
  if (dropdown) dropdown.classList.remove('show');

  document.getElementById('queryInput').value = p.query;
  document.getElementById('corpusInput').value = p.corpus;
  document.getElementById('candidateResponseInput').value = p.candidate;

  executeFullPipeline();

  const chatInput = document.getElementById('chatInputText');
  if (chatInput) {
    chatInput.value = p.query;
    autoResizeTextarea(chatInput);
  }
}

function updateParams() {
  document.getElementById('rrfKVal').textContent = document.getElementById('rrfKRange').value;
  document.getElementById('rerankThresholdVal').textContent = document.getElementById('rerankThresholdRange').value;
  document.getElementById('embedDimVal').textContent = document.getElementById('embedDimRange').value + "D";
  executeFullPipeline();
}

// ==========================================================================
// 5. SCIENTIFIC VISUALIZERS (Rich & High-Tech ECharts)
// ==========================================================================
async function executeFullPipeline() {
  const query = document.getElementById('queryInput').value.trim();
  const corpusRaw = document.getElementById('corpusInput').value.trim();
  const candidate = document.getElementById('candidateResponseInput').value.trim();
  const rrfK = parseInt(document.getElementById('rrfKRange').value, 10);
  const rerankThreshold = parseFloat(document.getElementById('rerankThresholdRange').value);
  const dimensions = parseInt(document.getElementById('embedDimRange').value, 10);

  if (!query || !corpusRaw) return;

  try {
    const data = await api.evaluateLab({
      query: query,
      corpus: corpusRaw,
      candidateResponse: candidate,
      rrfK: rrfK,
      rerankThreshold: rerankThreshold,
      dimensions: dimensions,
      maxTokensPerChunk: 256,
      overlapTokens: 32
    });

    renderVectorSpace(data.query, data.queryVector, data.docVectors);
    renderRagTriad(data.ragTriad);
    renderHardwareMetrics(data.hardwareProfiling, data.rankingMetrics);
    renderGroundingExplorer(data.ragTriad.sentenceDetails, data.ragTriad.faithfulness);
  } catch (err) {
    console.error("Lab evaluation error:", err);
  }
}

// 1. Vector Space & Cross-Document Topological Knowledge Graph
function renderVectorSpace(query, queryVector, docVectors) {
  const dom = document.getElementById('vectorSpaceChart');
  if (!dom) return;
  if (!vectorChart) vectorChart = echarts.init(dom);

  const queryPoint = [0, 0];
  const docs = docVectors || [];
  const numDocs = docs.length || 1;

  // Distinct color palette per Document
  const DOC_PALETTE = [
    '#10b981', '#38bdf8', '#a855f7', '#f59e0b', '#ec4899', 
    '#06b6d4', '#84cc16', '#eab308', '#f97316', '#6366f1', '#14b8a6'
  ];

  // Group chunks by Document ID
  const docGroups = {};
  docs.forEach((dv, idx) => {
    // Extract base doc name (e.g. PAROL-500.pdf_c1 -> PAROL-500.pdf)
    let rawDocId = dv.documentId || `Chunk ${idx + 1}`;
    let baseDoc = rawDocId.replace(/_c[0-9]+$/, '').replace(/#Sayfa_[0-9]+$/, '');
    if (!docGroups[baseDoc]) {
      docGroups[baseDoc] = [];
    }
    docGroups[baseDoc].push({ dv, originalIndex: idx });
  });

  const uniqueDocs = Object.keys(docGroups);
  const docColorMap = {};
  uniqueDocs.forEach((dName, dIdx) => {
    docColorMap[dName] = DOC_PALETTE[dIdx % DOC_PALETTE.length];
  });

  // Calculate clustered positions per document
  const docPoints = [];
  let currentAngle = -Math.PI / 2;
  const angleStepPerDoc = (2 * Math.PI) / (uniqueDocs.length || 1);

  uniqueDocs.forEach((dName, dIdx) => {
    const group = docGroups[dName];
    const docColor = docColorMap[dName];
    const startAngle = currentAngle;
    const groupAngleSpan = Math.min(angleStepPerDoc * 0.85, (group.length * 0.25));

    group.forEach((item, itemIdx) => {
      const dv = item.dv;
      const cos = dv.cosineDistance ?? 0.5;
      const euc = dv.euclideanDistance ?? 0.5;
      const r = Math.max(0.12, Math.min(1.0, cos * 1.1));
      
      const angle = startAngle + (group.length > 1 ? (itemIdx / (group.length - 1)) * groupAngleSpan : (groupAngleSpan / 2));
      const x = parseFloat((r * Math.cos(angle)).toFixed(3));
      const y = parseFloat((r * Math.sin(angle)).toFixed(3));

      docPoints.push({
        name: dv.documentId || `${dName} #${itemIdx + 1}`,
        baseDoc: dName,
        chunkIndex: itemIdx,
        color: docColor,
        value: [x, y, cos, euc],
        content: dv.content || ''
      });
    });

    currentAngle += angleStepPerDoc;
  });

  // 1. Ray Lines (Center Query -> Top 5 Nearest Chunks)
  const sortedByCos = [...docPoints].sort((a, b) => a.value[2] - b.value[2]);
  const topNearest = sortedByCos.slice(0, Math.min(5, docPoints.length));
  const queryRayLines = topNearest.map(dp => ({
    coords: [queryPoint, [dp.value[0], dp.value[1]]],
    lineStyle: {
      color: 'rgba(56, 189, 248, 0.75)',
      width: Math.max(1.5, (1.0 - dp.value[2]) * 3),
      type: 'dashed'
    }
  }));

  // 2. Intra-Document Sequential Links (Chunk i -> Chunk i+1 of same PDF)
  const intraDocLines = [];
  uniqueDocs.forEach(dName => {
    const pointsInDoc = docPoints.filter(p => p.baseDoc === dName);
    const dColor = docColorMap[dName];
    for (let i = 0; i < pointsInDoc.length - 1; i++) {
      intraDocLines.push({
        coords: [
          [pointsInDoc[i].value[0], pointsInDoc[i].value[1]],
          [pointsInDoc[i + 1].value[0], pointsInDoc[i + 1].value[1]]
        ],
        lineStyle: {
          color: dColor,
          width: 1.8,
          opacity: 0.6
        }
      });
    }
  });

  // 3. Cross-Document Semantic Affinity Bridges (High semantic similarity between different PDFs)
  const crossDocBridges = [];
  if (uniqueDocs.length > 1) {
    for (let i = 0; i < docPoints.length; i++) {
      for (let j = i + 1; j < docPoints.length; j++) {
        if (docPoints[i].baseDoc !== docPoints[j].baseDoc) {
          const distDiff = Math.abs(docPoints[i].value[2] - docPoints[j].value[2]);
          // If two chunks from different docs share very close semantic proximity to query
          if (distDiff < 0.06 && docPoints[i].value[2] < 0.5) {
            crossDocBridges.push({
              coords: [
                [docPoints[i].value[0], docPoints[i].value[1]],
                [docPoints[j].value[0], docPoints[j].value[1]]
              ],
              lineStyle: {
                color: 'rgba(168, 85, 247, 0.45)',
                width: 1.2,
                type: 'dotted'
              }
            });
          }
        }
      }
    }
  }

  const allLines = [...queryRayLines, ...intraDocLines, ...crossDocBridges];

  vectorChart.setOption({
    backgroundColor: 'transparent',
    legend: {
      show: uniqueDocs.length > 1,
      top: '2%',
      left: '3%',
      textStyle: { color: '#94a3b8', fontSize: 10.5, fontFamily: 'Inter' },
      data: uniqueDocs.map(d => ({ name: d, itemStyle: { color: docColorMap[d] } }))
    },
    tooltip: {
      trigger: 'item',
      backgroundColor: '#0f172a',
      borderColor: '#38bdf8',
      borderWidth: 1,
      padding: [8, 12],
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code', fontSize: 11 },
      formatter: function (p) {
        if (p.seriesType === 'scatter') {
          if (p.data.name === 'QUERY') {
            return `<div style="font-weight:700; color:#38bdf8; margin-bottom:4px;">MERKEZ SORGU (QUERY)</div>
                    <div style="color:#cbd5e1; font-style:italic; font-size:10.5px; margin-bottom:4px;">"${escapeHtml((query || '').slice(0, 55))}..."</div>
                    <div style="font-size:10px; color:#64748b;">Koordinat: [0.0, 0.0]</div>`;
          }
          const cos = p.data.value[2];
          const euc = p.data.value[3];
          const docName = p.data.baseDoc || 'Belge';
          const nodeColor = p.data.color || '#38bdf8';

          return `<div style="font-weight:700; color:${nodeColor}; margin-bottom:4px;">📄 ${escapeHtml(docName)}</div>
                  <div style="color:#ffffff; font-weight:600; font-size:11px; margin-bottom:4px;">${escapeHtml(p.data.name)}</div>
                  <div style="display:flex; justify-content:space-between; gap:12px; margin-bottom:2px;">
                    <span style="color:#94a3b8;">Cosine Mesafe:</span>
                    <strong style="color:${cos < 0.4 ? '#10b981' : (cos < 0.7 ? '#38bdf8' : '#f43f5e')};">${cos}</strong>
                  </div>
                  <div style="display:flex; justify-content:space-between; gap:12px; margin-bottom:4px;">
                    <span style="color:#94a3b8;">Euclidean (L2):</span>
                    <strong style="color:#38bdf8;">${euc}</strong>
                  </div>
                  <div style="color:#64748b; font-size:10px; max-width:240px; line-height:1.3;">
                    ${escapeHtml((p.data.content || '').slice(0, 75))}...
                  </div>`;
        }
      }
    },
    grid: { left: '8%', right: '8%', top: uniqueDocs.length > 1 ? '16%' : '10%', bottom: '10%' },
    xAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
      axisLine: { lineStyle: { color: '#334155' } },
      axisLabel: { color: '#64748b', fontSize: 10, fontFamily: 'Fira Code' }
    },
    yAxis: {
      type: 'value',
      scale: true,
      splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } },
      axisLine: { lineStyle: { color: '#334155' } },
      axisLabel: { color: '#64748b', fontSize: 10, fontFamily: 'Fira Code' }
    },
    series: [
      {
        type: 'lines',
        coordinateSystem: 'cartesian2d',
        data: allLines,
        effect: {
          show: true,
          period: 3.5,
          trailLength: 0.25,
          symbol: 'arrow',
          symbolSize: 4.5,
          color: '#38bdf8'
        }
      },
      {
        name: 'Query',
        type: 'scatter',
        symbol: 'diamond',
        symbolSize: 22,
        itemStyle: {
          color: '#0ea5e9',
          shadowBlur: 16,
          shadowColor: 'rgba(14, 165, 233, 0.8)'
        },
        label: {
          show: true,
          formatter: 'MERKEZ SORGU',
          position: 'bottom',
          color: '#38bdf8',
          fontFamily: 'Inter',
          fontWeight: '600',
          fontSize: 9.5
        },
        data: [{ name: 'QUERY', value: queryPoint }]
      },
      {
        name: 'Documents',
        type: 'scatter',
        symbolSize: val => 12 + ((1.0 - (val[2] || 0.5)) * 12),
        itemStyle: {
          color: p => p.data.color || '#38bdf8',
          shadowBlur: 10,
          shadowColor: 'rgba(56, 189, 248, 0.4)',
          opacity: 0.95
        },
        label: {
          show: true,
          formatter: p => numDocs > 15 ? (p.data.value[2] < 0.4 ? p.data.name : '') : p.data.name,
          position: 'top',
          color: '#cbd5e1',
          fontFamily: 'Inter',
          fontWeight: '600',
          fontSize: 9.5
        },
        data: docPoints
      }
    ]
  });
}

// 2. RAG Triad Radar with glowing area gradient
function renderRagTriad(ragTriad) {
  const dom = document.getElementById('ragTriadChart');
  if (!dom) return;
  if (!triadChart) triadChart = echarts.init(dom);

  const cr = (ragTriad.ContextRelevancy || ragTriad.contextRelevancy || 0) * 100;
  const ft = (ragTriad.Faithfulness || ragTriad.faithfulness || 0) * 100;
  const ar = (ragTriad.AnswerRelevancy || ragTriad.answerRelevancy || 0) * 100;

  triadChart.setOption({
    backgroundColor: 'transparent',
    tooltip: {
      trigger: 'axis',
      backgroundColor: '#0f172a',
      borderColor: '#38bdf8',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code' }
    },
    radar: {
      indicator: [
        { name: `Context Relevancy\n(%${cr.toFixed(0)} Arama Hassasiyeti)`, max: 100 },
        { name: `Faithfulness\n(%${ft.toFixed(0)} Olgusal Sadakat)`, max: 100 },
        { name: `Answer Relevancy\n(%${ar.toFixed(0)} Soru Uyumu)`, max: 100 }
      ],
      shape: 'polygon',
      splitNumber: 4,
      axisName: { color: '#cbd5e1', fontSize: 10.5, fontWeight: '600', fontFamily: 'Inter' },
      splitLine: { lineStyle: { color: 'rgba(56, 189, 248, 0.15)' } },
      splitArea: {
        show: true,
        areaStyle: {
          color: ['rgba(14, 165, 233, 0.03)', 'rgba(99, 102, 241, 0.05)', 'rgba(14, 165, 233, 0.07)', 'rgba(99, 102, 241, 0.1)']
        }
      },
      axisLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.08)' } }
    },
    series: [{
      type: 'radar',
      data: [{
        value: [cr, ft, ar],
        name: 'RAG Triad Tri-Metric',
        areaStyle: {
          color: new echarts.graphic.RadialGradient(0.5, 0.5, 1, [
            { offset: 0, color: 'rgba(56, 189, 248, 0.5)' },
            { offset: 1, color: 'rgba(99, 102, 241, 0.25)' }
          ])
        },
        lineStyle: { color: '#38bdf8', width: 2.5, shadowBlur: 8, shadowColor: 'rgba(56, 189, 248, 0.6)' },
        itemStyle: { color: '#ffffff', borderColor: '#38bdf8', borderWidth: 2 }
      }]
    }]
  });
}

// 3. Hardware Latency Bar Chart with multi-color components
function renderHardwareMetrics(hw, ranking) {
  const dom = document.getElementById('hardwareProfilingChart');
  if (!dom) return;
  if (!hardwareChart) hardwareChart = echarts.init(dom);

  const colors = [
    new echarts.graphic.LinearGradient(0, 0, 1, 0, [{ offset: 0, color: '#3b82f6' }, { offset: 1, color: '#60a5fa' }]),
    new echarts.graphic.LinearGradient(0, 0, 1, 0, [{ offset: 0, color: '#d97706' }, { offset: 1, color: '#fbbf24' }]),
    new echarts.graphic.LinearGradient(0, 0, 1, 0, [{ offset: 0, color: '#7c3aed' }, { offset: 1, color: '#a78bfa' }]),
    new echarts.graphic.LinearGradient(0, 0, 1, 0, [{ offset: 0, color: '#059669' }, { offset: 1, color: '#34d399' }]),
    new echarts.graphic.LinearGradient(0, 0, 1, 0, [{ offset: 0, color: '#0284c7' }, { offset: 1, color: '#38bdf8' }])
  ];

  const totalMs = hw.totalLatencyMs || hw.TotalLatencyMs || 0;
  const gpuMs = hw.gpuRerankMs || hw.GpuRerankMs || 0;
  const top1Score = (ranking && (ranking.top1Score ?? ranking.Top1Score)) ?? (ranking && (ranking.ndcgAtK ?? ranking.NdcgAtK)) ?? 0.88;

  hardwareChart.setOption({
    backgroundColor: 'transparent',
    title: {
      text: `Toplam: ${totalMs} ms  |  GPU: ${gpuMs} ms  |  Top-1 Skor: ${typeof top1Score === 'number' ? top1Score.toFixed(2) : top1Score}`,
      textStyle: { color: '#fbbf24', fontSize: 10.5, fontFamily: 'Fira Code', fontWeight: '600' },
      right: '3%',
      top: '4%'
    },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      backgroundColor: '#0f172a',
      borderColor: '#fbbf24',
      textStyle: { color: '#f8fafc', fontFamily: 'Fira Code' }
    },
    grid: { left: '20%', right: '8%', bottom: '10%', top: '18%', containLabel: true },
    xAxis: {
      type: 'value',
      name: 'ms',
      nameTextStyle: { color: '#64748b' },
      axisLabel: { color: '#94a3b8', fontSize: 10, fontFamily: 'Fira Code' },
      splitLine: { lineStyle: { color: 'rgba(255, 255, 255, 0.05)' } }
    },
    yAxis: {
      type: 'category',
      data: ['Chunk / Ingest', 'BM25 Sparse', 'SIMD Dense', hw.gpuDevice || 'GPU Re-Rank', 'Triad Değerlendirme'],
      axisLabel: { color: '#f1f5f9', fontSize: 10.5, fontWeight: '500' }
    },
    series: [{
      type: 'bar',
      itemStyle: {
        color: p => colors[p.dataIndex],
        borderRadius: [0, 4, 4, 0]
      },
      label: {
        show: true,
        position: 'right',
        formatter: '{c} ms',
        color: '#e2e8f0',
        fontFamily: 'Fira Code',
        fontSize: 10,
        fontWeight: '600'
      },
      data: [
        hw.ingestMs || hw.IngestMs || 0.1,
        hw.bm25Ms || hw.Bm25Ms || 0.1,
        hw.simdDenseMs || hw.SimdDenseMs || 0.1,
        hw.gpuRerankMs || hw.GpuRerankMs || 0.5,
        hw.evaluatorMs || hw.EvaluatorMs || 0.1
      ]
    }]
  });
}

// 4. Grounding Proof Explorer DOM renderer
function renderGroundingExplorer(sentenceDetails, overallFaithfulness) {
  const container = document.getElementById('groundingProofExplorer');
  if (!container) return;

  if (!sentenceDetails || sentenceDetails.length === 0) {
    container.innerHTML = `<div class="proof-empty">Doğrulanacak cümle bulunamadı.</div>`;
    return;
  }

  let html = '';
  sentenceDetails.forEach((s, idx) => {
    const isGrounded = s.isGrounded || s.IsGrounded;
    const ratio = Math.round((s.supportRatio || s.SupportRatio || 0) * 100);
    const sent = s.sentence || s.Sentence;
    const doc = s.bestMatchingDoc || s.BestMatchingDoc || 'Klinik Havuz';
    const snippet = s.sourceSnippet || s.SourceSnippet || '';

    const badgeClass = isGrounded ? 'tag-green' : 'tag-red';
    const badgeText = isGrounded ? `%${ratio} Doğrulandı` : `%${ratio} Desteksiz`;

    html += `
      <div class="proof-card ${isGrounded ? 'grounded' : 'ungrounded'}">
        <div class="proof-card-top">
          <span class="proof-status-tag ${badgeClass}">${badgeText}</span>
          <span class="proof-doc-tag">${escapeHtml(doc)}</span>
        </div>
        <div class="proof-sent-txt">"${escapeHtml(sent)}"</div>
        ${snippet ? `<div class="proof-snippet-txt">Kaynak Eşleşmesi: <span>"${escapeHtml(snippet)}"</span></div>` : ''}
      </div>
    `;
  });

  container.innerHTML = html;
}

function toggleFullscreenViz(boxId) {
  const box = document.getElementById(boxId);
  if (!box) return;
  box.classList.toggle('fullscreen-viz');
  resizeAllCharts();
}

function resizeAllCharts() {
  if (vectorChart) vectorChart.resize();
  if (triadChart) triadChart.resize();
  if (hardwareChart) hardwareChart.resize();
}

// ==========================================================================
// 6. CLINICAL CHAT CONTROLLER
// ==========================================================================
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
  autoResizeTextarea(input);
  sendChatMessage();
}

async function sendChatMessage() {
  const input = document.getElementById('chatInputText');
  const query = input.value.trim();
  if (!query) return;

  const stream = document.getElementById('chatMessagesStream');

  // 1. User Message Bubble
  const userHtml = `
    <div class="msg-card msg-user">
      <div class="msg-header">Kullanıcı / Doktor</div>
      <div class="msg-body">${escapeHtml(query)}</div>
    </div>
  `;
  stream.insertAdjacentHTML('beforeend', userHtml);
  input.value = '';
  autoResizeTextarea(input);
  stream.scrollTop = stream.scrollHeight;

  // 2. Pending AI Bubble
  const pendingId = `ai_msg_${Date.now()}`;
  const pendingHtml = `
    <div id="${pendingId}" class="msg-card msg-assistant">
      <div class="msg-header">
        <span class="msg-sender">Klinik Asistan</span>
        <span class="msg-tag">İşleniyor...</span>
      </div>
      <div class="msg-body" style="color: var(--text-muted);">
        Dokümanlar taranıyor, GPU re-ranking ve olgusal grounding denetleniyor...
      </div>
    </div>
  `;
  stream.insertAdjacentHTML('beforeend', pendingHtml);
  stream.scrollTop = stream.scrollHeight;

  // 3. API Call
  try {
    const corpusInput = document.getElementById('corpusInput');
    const corpusVal = corpusInput ? corpusInput.value.trim() : (state.uploadedCorpus || '');
    const docName = state.uploadedDocName || (corpusVal ? "Yuklenen_Dokuman.pdf" : "Klinik_Korpus.pdf");

    const data = await api.sendChat(query, corpusVal, docName);
    state.currentTelemetry = data;
    renderAiResponse(pendingId, data);
    updateRagInspector(data);
  } catch (err) {
    const elem = document.getElementById(pendingId);
    if (elem) {
      elem.querySelector('.msg-tag').className = 'msg-tag blocked';
      elem.querySelector('.msg-tag').textContent = 'Hata';
      elem.querySelector('.msg-body').innerHTML = `<span style="color: var(--danger);">API Yanıtı Alınamadı: ${escapeHtml(err.message)}</span>`;
    }
  }
}

function renderAiResponse(elemId, data) {
  const elem = document.getElementById(elemId);
  if (!elem) return;

  state.currentSentences = data.sentences || [];

  const tagElem = elem.querySelector('.msg-tag');
  const bodyElem = elem.querySelector('.msg-body');

  let hasUngrounded = false;
  let formattedHtml = '';

  (data.sentences || []).forEach((s, idx) => {
    const isGrounded = s.isGrounded || false;
    const doc = s.bestDoc || 'Bilinmiyor';
    const isMeta = doc.includes('Diyalog') || doc.includes('Meta');

    if (!isGrounded && !isMeta) {
      hasUngrounded = true;
    }

    const cssClass = isMeta ? 'conversational' : (isGrounded ? 'grounded' : 'ungrounded');
    const ratio = Math.round((s.supportRatio || 0) * 100);
    const titleText = isMeta ? 'Sohbet Yanıtı' : (isGrounded ? `Olgusal Kanıt: %${ratio} Doğrulandı` : `Desteksiz İddia: %${ratio}`);

    const sentenceText = s.sentence || '';
    // Check if sentence starts with a numbered list item like "1. ", "2. ", or bullet
    const isListItem = /^[0-9]+[\.\)]\s+|^[-*•]\s+/.test(sentenceText.trim());

    if (isListItem && idx > 0) {
      formattedHtml += '<div class="chat-list-spacer"></div>';
    }

    formattedHtml += `
      <span class="sentence-span ${cssClass}" 
            data-idx="${idx}"
            title="${titleText} (Detaylar için tıklayın)"
            onmouseenter="onSentenceHover(${idx})"
            onclick="onSentenceClick(${idx})">${escapeHtml(sentenceText)}</span> `;
  });

  if (hasUngrounded) {
    tagElem.className = 'msg-tag blocked';
    tagElem.textContent = 'Güvenlik Uyarısı';
  } else {
    tagElem.className = 'msg-tag';
    tagElem.textContent = 'Olgusal Olarak Doğrulandı';
  }

  bodyElem.innerHTML = formattedHtml || escapeHtml(data.answer || 'Yanıt alınamadı.');
}

function onSentenceHover(idx) {
  if (!state.currentSentences || !state.currentSentences[idx]) return;

  document.querySelectorAll('.sentence-span').forEach(el => el.classList.remove('active'));
  const activeEl = document.querySelector(`.sentence-span[data-idx="${idx}"]`);
  if (activeEl) activeEl.classList.add('active');

  const s = state.currentSentences[idx];
  const doc = s.bestDoc || 'Doküman';
  const isMeta = doc.includes('Diyalog') || doc.includes('Meta');
  const ratio = Math.round((s.supportRatio || 0) * 100);

  updateSentenceProofBox(s.sentence || '', doc, s.snippet || '', s.isGrounded || false, ratio, isMeta);
}

function onSentenceClick(idx) {
  onSentenceHover(idx);
  const s = state.currentSentences ? state.currentSentences[idx] : null;
  const doc = (s && s.bestDoc) || '';
  const isMeta = doc.includes('Diyalog') || doc.includes('Meta');
  if (!isMeta) {
    openSourceDocViewer();
  }
}

// ==========================================================================
// 7. RAG INSPECTOR CONTROLLER
// ==========================================================================
function updateRagInspector(data) {
  if (!data || !data.telemetry) return;
  const t = data.telemetry;

  document.getElementById('activeMessageBadge').textContent = data.messageId || 'msg_live';

  // Metrics
  document.getElementById('inspConfidence').textContent = formatNumber(t.retrievalConfidence, 2);
  document.getElementById('inspRerankLift').textContent = `+${formatNumber(t.rerankLiftPercent, 1)}%`;
  document.getElementById('inspContextRel').textContent = `${formatNumber(t.contextRelevancyPercent, 0)}%`;
  document.getElementById('inspFaithfulness').textContent = `${formatNumber(t.faithfulnessPercent, 0)}%`;

  // Dosage Lock Banner
  const dosageBox = document.getElementById('dosageLockBox');
  const dosageTitle = document.getElementById('dosageLockTitle');
  const dosageDesc = document.getElementById('dosageLockDesc');
  const dg = t.dosageGuard || {};

  if (dg.isValid !== false) {
    dosageBox.className = "guard-banner";
    dosageTitle.textContent = "Dozaj & Etken Madde Kilidi: Doğrulandı";
    dosageDesc.textContent = dg.status || "Klinik dozaj ve kısıtlamalar güvenli aralıkta.";
  } else {
    dosageBox.className = "guard-banner violation";
    dosageTitle.textContent = "Dozaj & Etken Madde Kilidi: İhlal / Engel";
    dosageDesc.textContent = dg.status || "Belirtilen doz belgedeki yasal sınırı aşıyor.";
  }

  // Hardware Latency
  const lms = t.latencyMs || {};
  document.getElementById('inspTotalTime').textContent = `${lms.total ?? '---'} ms`;
  document.getElementById('inspBm25Ms').textContent = `${lms.bm25 ?? 0} ms`;
  document.getElementById('inspSimdMs').textContent = `${lms.simdVector ?? 0} ms`;
  document.getElementById('inspGpuMs').textContent = `${lms.gpuRerank ?? 0} ms`;

  const total = lms.total || 100;
  document.getElementById('barSimd').style.width = Math.min(100, ((lms.simdVector || 1) / total) * 300) + '%';
  document.getElementById('barBm25').style.width = Math.min(100, ((lms.bm25 || 1) / total) * 300) + '%';
  document.getElementById('barGpu').style.width = Math.min(100, ((lms.gpuRerank || 1) / total) * 300) + '%';

  // Trace Steps
  const stepsContainer = document.getElementById('decisionPipelineSteps');
  if (stepsContainer) {
    const totalSentences = (data.sentences || []).length;
    const groundedCount = (data.sentences || []).filter(s => s.isGrounded || (s.bestDoc && (s.bestDoc.includes('Diyalog') || s.bestDoc.includes('Meta')))).length;
    const ungroundedCount = totalSentences - groundedCount;
    const topDoc = (t.retrievedChunks && t.retrievedChunks[0]) ? t.retrievedChunks[0].doc : 'Doküman';
    const topScore = (t.retrievedChunks && t.retrievedChunks[0]) ? (t.retrievedChunks[0].score || 0).toFixed(2) : '0.00';
    const dgValid = (t.dosageGuard && t.dosageGuard.isValid !== false);

    stepsContainer.innerHTML = `
      <div class="trace-step">
        <span class="step-index">1</span>
        <span class="step-title">Vektörizasyon &amp; SIMD Embedding (${lms.simdVector ?? 0} ms)</span>
        <span class="step-tag green">CPU AVX2</span>
      </div>
      <div class="trace-step">
        <span class="step-index">2</span>
        <span class="step-title">Hibrit Arama (BM25: ${lms.bm25 ?? 0} ms) + RRF</span>
        <span class="step-tag green">K=60 Fusion</span>
      </div>
      <div class="trace-step">
        <span class="step-index">3</span>
        <span class="step-title">RTX 4060 Ti Cross-Encoder ➔ <em>${escapeHtml(topDoc)}</em> (${topScore})</span>
        <span class="step-tag yellow">${lms.gpuRerank ?? 0} ms GPU</span>
      </div>
      <div class="trace-step">
        <span class="step-index">4</span>
        <span class="step-title">Ollama ${escapeHtml(t.llmModel || 'qwen2.5:7b')} Klinik Üretim (${totalSentences} Cümle)</span>
        <span class="step-tag">${lms.llmGenerate ?? 0} ms</span>
      </div>
      <div class="trace-step">
        <span class="step-index">5</span>
        <span class="step-title">Olgusal Grounding (${groundedCount}/${totalSentences} Doğrulandı)</span>
        <span class="step-tag ${dgValid && ungroundedCount === 0 ? 'green' : 'red'}">${dgValid && ungroundedCount === 0 ? 'Onaylandı' : 'Uyarı'}</span>
      </div>
    `;
  }

  // Raw JSON Payload
  document.getElementById('rawTelemetryJson').textContent = JSON.stringify(data, null, 2);

  if (data.sentences && data.sentences.length > 0) {
    onSentenceHover(0);
  }
}

function updateSentenceProofBox(sentence, doc, snippet, isGrounded, ratio, isMeta = false) {
  const container = document.getElementById('activeSentenceProofContent');
  const lineIndicator = document.getElementById('activeLineIndicator');
  const openDocBtn = document.getElementById('openDocViewerBtn');

  const corpusInput = document.getElementById('corpusInput');
  let fullCorpus = (corpusInput && corpusInput.value.trim()) ? corpusInput.value.trim() : (state.uploadedCorpus || '');

  let matchedLineIndex = -1;
  if (fullCorpus && !isMeta) {
    matchedLineIndex = findMatchingLineInCorpus(snippet, sentence, fullCorpus);
  }

  state.activeTargetLineNumber = matchedLineIndex >= 0 ? (matchedLineIndex + 1) : null;
  state.activeTargetDoc = doc;
  state.activeTargetSnippet = snippet;
  state.activeTargetSentence = sentence;

  if (matchedLineIndex >= 0 && !isMeta) {
    lineIndicator.textContent = `Satır #${matchedLineIndex + 1}`;
    lineIndicator.style.display = 'inline-block';
    openDocBtn.style.display = 'inline-flex';
  } else if (isMeta) {
    lineIndicator.textContent = `Diyalog`;
    lineIndicator.style.display = 'inline-block';
    openDocBtn.style.display = 'none';
  } else {
    lineIndicator.style.display = 'none';
    openDocBtn.style.display = 'none';
  }

  let badgeClass = isMeta ? 'blue' : (isGrounded ? 'green' : 'red');
  let badgeTitle = isMeta ? 'Genel Klinik Diyalog / Selamlama' : (isGrounded ? 'Olgusal Kanıt Doğrulandı' : 'Desteksiz İddia / Halüsinasyon');
  let badgePct = isMeta ? 'Diyalog' : `%${ratio} Destek`;

  let lineBadgeHtml = (matchedLineIndex >= 0 && !isMeta)
    ? `<div class="proof-chip line">Satır #${matchedLineIndex + 1}</div>`
    : '';

  container.innerHTML = `
    <div class="proof-card-layout">
      <div class="proof-badge-banner ${badgeClass}">
        <span class="proof-badge-text">${badgeTitle}</span>
        <span class="proof-badge-pct">${badgePct}</span>
      </div>
      
      <div class="proof-block">
        <div class="proof-block-label">Yanıttaki İddia:</div>
        <div class="proof-claim-text">"${escapeHtml(sentence)}"</div>
      </div>

      <div class="proof-citation-row">
        <div class="proof-chip doc">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
          <span>${escapeHtml(doc)}</span>
        </div>
        ${lineBadgeHtml}
      </div>

      ${snippet ? `
        <div class="proof-block snippet-block">
          <div class="proof-block-label">PDF / Korpus Eşleşen Paragraf:</div>
          <div class="proof-source-text">"${escapeHtml(snippet)}"</div>
        </div>
      ` : ''}
    </div>
  `;
}

function findMatchingLineInCorpus(snippet, sentence, fullCorpus) {
  if (!fullCorpus) return -1;
  const lines = fullCorpus.split('\n');

  if (snippet) {
    const cleanSnippet = snippet.replace(/^[0-9]\.\s*/, '').trim().toLowerCase();
    if (cleanSnippet.length > 6) {
      const targetSnippet = cleanSnippet.slice(0, 30);
      const idx = lines.findIndex(l => l.toLowerCase().includes(targetSnippet));
      if (idx >= 0) return idx;
    }
  }

  const cleanSentence = (sentence || '').toLowerCase().replace(/[.,\/#!$%\^&\*;:{}=\-_`~()?"'0-9]/g, ' ');
  const words = cleanSentence.split(/\s+/).filter(w => w.length >= 4);

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

// ==========================================================================
// 8. MODALS & BENCHMARK CONTROLLER
// ==========================================================================
function openBenchmarkModal() {
  document.getElementById('benchmarkModal').style.display = 'flex';
}
function closeBenchmarkModal() {
  document.getElementById('benchmarkModal').style.display = 'none';
}

function openChunksModal() {
  const modal = document.getElementById('chunksModal');
  const tbody = document.getElementById('chunksTableTbody');
  const chunks = state.uploadedChunks || [];

  modal.style.display = 'flex';

  if (chunks.length === 0) {
    tbody.innerHTML = `<tr><td colspan="4" class="text-center text-muted" style="padding: 2rem;">Henüz ayrıştırılmış chunk bulunamadı.</td></tr>`;
    return;
  }

  let html = '';
  chunks.forEach((c, i) => {
    const docId = c.documentId || c.DocumentId || `Bölüm_${i + 1}`;
    const len = c.length || c.Length || (c.content ? c.content.length : 0);
    const content = escapeHtml(c.content || c.Content || '');
    html += `
      <tr>
        <td style="color: var(--text-muted); font-family: var(--font-mono);">${i + 1}</td>
        <td style="font-weight: 500; color: var(--primary);">${escapeHtml(docId)}</td>
        <td style="font-family: var(--font-mono); color: var(--text-secondary);">${len} char</td>
        <td style="font-family: var(--font-mono); font-size: 0.72rem; color: var(--text-secondary); line-height: 1.4;">${content}</td>
      </tr>
    `;
  });
  tbody.innerHTML = html;
}
function closeChunksModal() {
  document.getElementById('chunksModal').style.display = 'none';
}

function openSourceDocViewer() {
  const corpusInput = document.getElementById('corpusInput');
  let fullCorpus = (corpusInput && corpusInput.value.trim()) ? corpusInput.value.trim() : (state.uploadedCorpus || '');

  const container = document.getElementById('sourceDocLinesContainer');
  const modal = document.getElementById('sourceDocModal');
  const targetBadge = document.getElementById('docViewerTargetBadge');
  const modalTitle = document.getElementById('sourceDocModalTitle');

  const docName = state.activeTargetDoc || state.uploadedDocName || 'Dokuman.pdf';
  modalTitle.textContent = `Kaynak Doküman: ${docName}`;
  const targetLine = state.activeTargetLineNumber || 1;
  targetBadge.textContent = `Hedef: Satır #${targetLine}`;

  const lines = (fullCorpus || "Doküman içeriği henüz yüklenmedi.").split('\n');
  let html = '';

  lines.forEach((line, idx) => {
    const lineNum = idx + 1;
    const isTarget = (lineNum === targetLine);
    const targetClass = isTarget ? 'doc-line active-highlight-line' : 'doc-line';
    const targetId = `doc_line_${lineNum}`;

    let lineContentHtml = escapeHtml(line || ' ');
    if (isTarget && state.activeTargetSentence) {
      const kw = state.activeTargetSentence.split(/\s+/).filter(w => w.length > 5);
      kw.forEach(w => {
        const regex = new RegExp(`(${escapeRegex(w)})`, 'gi');
        lineContentHtml = lineContentHtml.replace(regex, '<mark style="background:var(--warning); color:#000; padding:0 2px; border-radius:2px;">$1</mark>');
      });
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

  setTimeout(() => {
    const targetEl = document.getElementById(`doc_line_${targetLine}`);
    if (targetEl) targetEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }, 100);
}
function closeSourceDocModal() {
  document.getElementById('sourceDocModal').style.display = 'none';
}

async function runLiveBenchmark() {
  const btn = document.getElementById('runBenchmarkBtn');
  const tbody = document.getElementById('benchmarkCasesTbody');

  btn.disabled = true;
  btn.textContent = "Testler Koşturuluyor...";
  tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted" style="padding: 2rem;">Gürültülü dokümanlar arasına gizlenmiş tıbbi senaryolar test ediliyor...</td></tr>`;

  try {
    const data = await api.runStressTest();

    document.getElementById('bmPassRate').textContent = `${data.passRate}% (${data.passedTests}/${data.totalTests})`;
    document.getElementById('bmFaithfulness').textContent = `${data.avgFaithfulness}%`;
    document.getElementById('bmTotalTime').textContent = `${data.totalLatencyMs} ms`;
    document.getElementById('bmGpuSpeed').textContent = data.gpuBenchmark.isGpuActive ? `${data.gpuBenchmark.latencyMs} ms` : "CPU";

    let rowsHtml = '';
    data.testCases.forEach(tc => {
      rowsHtml += `
        <tr>
          <td style="font-family:var(--font-mono); color:var(--text-muted);">${tc.id || tc.Id}</td>
          <td><strong>${escapeHtml(tc.name || tc.Name)}</strong></td>
          <td style="font-family:var(--font-mono); font-size:0.7rem; color:var(--text-secondary); max-width:240px;">${escapeHtml(tc.query || tc.Query)}</td>
          <td style="font-family:var(--font-mono); font-weight:600; color:${(tc.faithfulness || tc.Faithfulness) > 0.7 ? 'var(--success)' : 'var(--danger)'}">${((tc.faithfulness || tc.Faithfulness) * 100).toFixed(0)}%</td>
          <td style="font-family:var(--font-mono);">${tc.latencyMs || tc.LatencyMs} ms</td>
          <td><span class="verdict-tag ${tc.passed || tc.Passed ? 'pass' : 'fail'}">${tc.verdict || tc.Verdict}</span></td>
        </tr>
      `;
    });

    if (data.gpuBenchmark && data.gpuBenchmark.isGpuActive) {
      rowsHtml += `
        <tr style="background: rgba(245, 158, 11, 0.05);">
          <td style="font-family:var(--font-mono); color:var(--warning);">GPU-01</td>
          <td><strong>${escapeHtml(data.gpuBenchmark.device || 'DirectML GPU')} Re-Ranking</strong></td>
          <td style="font-family:var(--font-mono); font-size:0.7rem; color:var(--text-secondary);">${escapeHtml(data.gpuBenchmark.topChunk || '')}</td>
          <td style="font-family:var(--font-mono); font-weight:600; color:var(--success);">%${(data.gpuBenchmark.topScore * 100).toFixed(1)}</td>
          <td style="font-family:var(--font-mono); font-weight:600; color:var(--warning);">${data.gpuBenchmark.latencyMs} ms</td>
          <td><span class="verdict-tag pass">GPU Zirve</span></td>
        </tr>
      `;
    }

    tbody.innerHTML = rowsHtml;
  } catch (err) {
    tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="color:var(--danger); padding:2rem;">Hata: ${escapeHtml(err.message)}</td></tr>`;
  } finally {
    btn.disabled = false;
    btn.textContent = "Testleri Yeniden Çalıştır";
  }
}

// ==========================================================================
// 9. FILE INGESTION & PERSISTENT CORPUS MANAGER (SQLite)
// ==========================================================================
function handleFileSelected(event) {
  const files = event.target.files;
  if (!files || files.length === 0) return;
  setupFilesForUpload(files);
}

function setupFilesForUpload(files) {
  const label = document.getElementById('uploadFileNameLabel');
  const btn = document.getElementById('uploadProcessBtn');
  const pwGroup = document.getElementById('pdfPasswordGroup');
  const status = document.getElementById('uploadStatusBadge');

  const fileCount = files.length;
  if (fileCount === 1) {
    const sizeKb = (files[0].size / 1024).toFixed(1);
    if (label) label.textContent = `${files[0].name} (${sizeKb} KB)`;
  } else {
    let totalSizeKb = 0;
    for (let i = 0; i < files.length; i++) totalSizeKb += files[i].size;
    totalSizeKb = (totalSizeKb / 1024).toFixed(1);
    if (label) label.textContent = `${fileCount} Dosya Seçildi (${totalSizeKb} KB Toplam)`;
  }

  if (btn) btn.style.display = 'inline-flex';
  if (status) status.style.display = 'none';

  let hasPdf = false;
  for (let i = 0; i < files.length; i++) {
    if (files[i].name.toLowerCase().endsWith('.pdf')) hasPdf = true;
  }
  if (hasPdf && pwGroup) {
    pwGroup.style.display = 'block';
  } else if (pwGroup) {
    pwGroup.style.display = 'none';
  }

  uploadAndIngestDocuments(files);
}

async function uploadAndIngestDocuments(filesList) {
  let targetFiles = filesList;
  if (!targetFiles || targetFiles.length === 0) {
    const inputFiles = document.getElementById('fileUploadInput').files;
    if (inputFiles && inputFiles.length > 0) targetFiles = inputFiles;
  }
  if (!targetFiles || targetFiles.length === 0) return;

  const btn = document.getElementById('uploadProcessBtn');
  const status = document.getElementById('uploadStatusBadge');
  const pwInput = document.getElementById('pdfPasswordInput');
  const password = pwInput ? pwInput.value.trim() : '';

  if (btn) {
    btn.disabled = true;
    btn.textContent = `${targetFiles.length} Dosya İşleniyor...`;
  }
  if (status) {
    status.style.display = 'block';
    status.className = 'upload-feedback';
    status.textContent = `${targetFiles.length} dosya sunucuya iletiliyor, PDF metinleri çıkarılıyor ve SQLite'a kaydediliyor...`;
  }

  try {
    const data = await api.uploadDocuments(targetFiles, password);

    const corpusInput = document.getElementById('corpusInput');
    if (corpusInput && data.combinedText) {
      corpusInput.value = data.combinedText;
    }

    state.uploadedCorpus = data.combinedText || '';
    state.uploadedDocName = data.processedFiles && data.processedFiles.length > 0 
      ? data.processedFiles.map(f => f.fileName).join(', ') 
      : 'Toplu_Korpus.pdf';
    state.uploadedChunks = data.chunks || [];

    if (status) {
      status.className = 'upload-feedback success';
      status.innerHTML = `<strong>${targetFiles.length} Belge Kaydedildi</strong> (${data.totalCorpusChunks} toplam chunk veritabanında).`;
    }

    const viewChunksBtn = document.getElementById('viewChunksBtn');
    if (viewChunksBtn && data.chunks && data.chunks.length > 0) {
      viewChunksBtn.style.display = 'inline-flex';
    }

    await loadCorpusStats();
    await executeFullPipeline();
  } catch (err) {
    if (status) {
      status.className = 'upload-feedback error';
      status.textContent = `Yükleme Hatası: ${err.message}`;
    }
  } finally {
    if (btn) {
      btn.disabled = false;
      btn.textContent = 'Yeni Dosyalar Yükle';
    }
  }
}

async function loadCorpusStats() {
  try {
    const data = await api.getDocuments();
    const badge = document.getElementById('corpusCountBadge');
    if (badge) badge.textContent = data.totalDocuments || '0';

    const chatBadge = document.getElementById('chatActiveDocBadge');
    if (chatBadge) {
      if (data.totalDocuments > 0) {
        chatBadge.textContent = `${data.totalDocuments} Belge • ${data.totalChunks} Chunk [SQLite]`;
      } else {
        chatBadge.textContent = 'Klinik Korpus (Varsayılan)';
      }
    }
  } catch (err) {
    console.error("Corpus stats load error:", err);
  }
}

function openCorpusModal() {
  const modal = document.getElementById('corpusModal');
  if (modal) {
    modal.style.display = 'flex';
    refreshCorpusModal();
  }
}

function closeCorpusModal() {
  const modal = document.getElementById('corpusModal');
  if (modal) modal.style.display = 'none';
}

async function refreshCorpusModal() {
  const tbody = document.getElementById('corpusTableTbody');
  const statDocs = document.getElementById('corpusStatDocs');
  const statChunks = document.getElementById('corpusStatChunks');
  const statChars = document.getElementById('corpusStatChars');

  if (tbody) {
    tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted" style="padding: 1.5rem;">Yükleniyor...</td></tr>`;
  }

  try {
    const data = await api.getDocuments();

    if (statDocs) statDocs.textContent = data.totalDocuments || 0;
    if (statChunks) statChunks.textContent = data.totalChunks || 0;
    if (statChars) statChars.textContent = data.totalCharacters ? data.totalCharacters.toLocaleString('tr-TR') : 0;

    if (!data.documents || data.documents.length === 0) {
      if (tbody) {
        tbody.innerHTML = `<tr><td colspan="6" class="text-center text-muted" style="padding: 2rem;">Henüz kalıcı SQLite veritabanında doküman bulunmuyor. Sol panelden veya Chat'ten PDF yükleyebilirsiniz.</td></tr>`;
      }
      return;
    }

    let rowsHtml = '';
    data.documents.forEach((d) => {
      const sizeKb = (d.fileSizeBytes / 1024).toFixed(1);
      const dateStr = d.uploadedAt ? new Date(d.uploadedAt).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', day: 'numeric', month: 'short' }) : '---';

      rowsHtml += `
        <tr>
          <td><strong style="color: var(--text-primary);">${escapeHtml(d.fileName)}</strong></td>
          <td style="font-family: var(--font-mono);">${d.totalPages} Sayfa</td>
          <td style="font-family: var(--font-mono);"><span class="table-tag" style="background:rgba(56,189,248,0.15); color:#38bdf8;">${d.totalChunks} Chunk</span></td>
          <td style="font-family: var(--font-mono); color: var(--text-muted);">${sizeKb} KB</td>
          <td style="font-size: 0.72rem; color: var(--text-muted);">${dateStr}</td>
          <td style="text-align: center;">
            <button class="btn btn-outline btn-sm text-danger" style="padding: 0.15rem 0.4rem; font-size: 0.68rem;" onclick="deleteCorpusDocument('${escapeHtml(d.id)}')">
              Sil
            </button>
          </td>
        </tr>
      `;
    });

    if (tbody) tbody.innerHTML = rowsHtml;
  } catch (err) {
    if (tbody) {
      tbody.innerHTML = `<tr><td colspan="6" class="text-center text-danger" style="padding: 1.5rem;">Hata: ${escapeHtml(err.message)}</td></tr>`;
    }
  }
}

async function deleteCorpusDocument(docId) {
  if (!confirm(`"${docId}" dokümanını ve tüm chunk'larını SQLite veritabanından silmek istediğinize emin misiniz?`)) {
    return;
  }

  try {
    await api.deleteDocument(docId);
    await refreshCorpusModal();
    await loadCorpusStats();
    await executeFullPipeline();
  } catch (err) {
    alert(`Silinemedi: ${err.message}`);
  }
}

async function clearEntireCorpus() {
  if (!confirm("DİKKAT: Veritabanındaki TÜM dokümanlar ve chunk'lar kalıcı olarak silinecek. Emin misiniz?")) {
    return;
  }

  try {
    await api.clearDocuments();
    await refreshCorpusModal();
    await loadCorpusStats();
    state.uploadedCorpus = null;
    state.uploadedDocName = null;
    state.uploadedChunks = [];
    await executeFullPipeline();
  } catch (err) {
    alert(`Temizlenemedi: ${err.message}`);
  }
}

function setupDragAndDrop() {
  const dropzone = document.getElementById('uploadDropzone');
  if (!dropzone) return;

  ['dragenter', 'dragover'].forEach(name => {
    dropzone.addEventListener(name, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropzone.classList.add('drag-over');
    }, false);
  });

  ['dragleave', 'drop'].forEach(name => {
    dropzone.addEventListener(name, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropzone.classList.remove('drag-over');
    }, false);
  });

  dropzone.addEventListener('drop', (e) => {
    const dt = e.dataTransfer;
    if (dt.files && dt.files.length > 0) {
      setupFilesForUpload(dt.files);
    }
  }, false);
}

// Global ESC key listener for modals & fullscreen
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    closeSourceDocModal();
    closeChunksModal();
    closeBenchmarkModal();
    closeCorpusModal();
    document.querySelectorAll('.chart-card.fullscreen-viz').forEach(el => el.classList.remove('fullscreen-viz'));
    resizeAllCharts();
  }
});

// Window resize handler
window.addEventListener('resize', resizeAllCharts);

// ==========================================================================
// 10. INITIALIZATION
// ==========================================================================
document.addEventListener('DOMContentLoaded', async () => {
  setupDragAndDrop();
  await checkApiHealth();
  await loadCorpusStats();
  // By default start in the active tab (chat or lab)
  await executeFullPipeline();
});
