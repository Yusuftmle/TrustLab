# 🛡️ TrustLab Mühendislik Günlüğü — Gün 2
**Tarih:** 19 Ağustos 2026  
**Odak:** Bilgi Getirme (Retrieval) Sanatı, Okapi BM25, SIMD Vektörler, RRF Füzyonu ve RTX 4060 Ti ile Canlı GPU Re-Ranking 🚀

---

## ☕ Günün Başlangıcı: "Sadece Vektör Veritabanı Yetmez mi?" Yanılgısı

Bugün masaya otururken RAG dünyasındaki en yaygın ve en tehlikeli ezberi masaya yatırdık:  
*"Metinleri embedding modeline verip vektör veritabanına (Vector DB) atsak, sonra da kosinüs benzerliği yapsak bize yetmez mi?"*

Cevabın koca bir **"HAYIR"** olduğunu çok çarpıcı bir şekilde gördük.  
Vektör modelleri genel konseptleri çok iyi yakalasa da; iş tıbbi dozajlara (`1000mg`), prospektüs kodlarına, teknik kısaltmalara (`i.v.`, `ICD-10`) veya ürün model numaralarına (`RTX-4060-Ti`) geldiğinde anlamsal uzayda bulanıklaşıyor (**Vector Dilution**). Yani tek başına vektör arama, samanlıktaki o kritik iğneyi sıklıkla kaçırıyor.

Bu açığı kapatmak için bugün bilgi getirme (Retrieval) hattımızı adım adım inşa ettik.

---

## 🧠 Bugün Yaşadığımız ve Çözdüğümüz 4 Büyük Mühendislik Aşaması

### 1. 30 Yıllık Efsane: Okapi BM25 ve Leksikal Hafıza (Sparse Indexing)
Vektörün kaçırdığı o harfi harfine eşleşmeleri yakalamak için 1990'lardan beri arama motorlarının omurgası olan **Okapi BM25** algoritmasını projemize dahil ettik:
* **Ters Belge Sıklığı (IDF):** *"ve"*, *"bir"* gibi her yerde geçen kelimelere ceza kesip; *"Penisilin"* veya *"Kontrendikasyon"* gibi nadir kelimeleri altın değerinde puanlıyor.
* **Kelime Doygunluğu ($k_1 = 1.5$):** Bir kelimenin 50 kere geçmesiyle 3 kere geçmesi arasında uçurum olmasını engelleyip spam'i bitiriyor.
* **Doküman Uzunluğu Cezası ($b = 0.75$):** Kısa bir doktor notunda kritik kelimenin geçmesini, 500 sayfalık tıp kitabında tesadüfen geçmesine karşı koruyor.

> **Kodumuz:** [`Bm25SparseIndex.cs`](../../src/TrustLab.Rag/Indexing/Bm25SparseIndex.cs)

---

### 2. Donanım Seviyesinde Hız: SIMD ile Dense Vektör Deposu
Embedding vektörleri arasındaki kosinüs benzerliğini hesaplarken standart bir C# `for` döngüsünün 1536 float sayıyı tek tek çarpmasının işlemciyi kilitleyeceğini konuştuk.
* **Çözümümüz:** .NET'in `TensorPrimitives.CosineSimilarity` kütüphanesini devreye soktuk.
* Bu kütüphane, işlemcinin (CPU) özel **AVX2 / AVX-512** vektör çekirdeklerine doğrudan talimat vererek 8-16 adet ondalıklı sayıyı tek bir saat vuruşunda (SIMD) paralel çarptı!

> **Kodumuz:** [`DenseVectorStore.cs`](../../src/TrustLab.Rag/Indexing/DenseVectorStore.cs#L54-L58)

---

### 3. Elmalarla Armutları Toplama Sanatı: RRF ($k=60$) Hibrit Füzyon
BM25 aramasından `14.85` gibi soyut bir puan çıkarken, Vektör aramasından `0.87` (kosinüs) çıkıyordu. İki farklı dünyanın puanını doğrudan toplayamazdık (**Score Incommensurability Problemi**).

İki uzmanın listesini puanlara hiç bakmadan, sadece **sıralama derecelerine (Rank)** göre adilce birleştiren **Reciprocal Rank Fusion (RRF)** formülünü kurduk:
$$RRF\_Score = \frac{1.0}{60 + \text{Rank}}$$
Hem BM25'in hem de Vektörün üst sıralara koyduğu parçalar otomatik olarak havuzun en tepesine fırladı!

> **Kodumuz:** [`ReciprocalRankFusion.cs`](../../src/TrustLab.Rag/Fusion/ReciprocalRankFusion.cs)

---

### 4. Günün Zirve Noktası: RTX 4060 Ti ile Canlı GPU Re-Ranking 🔥
*"Bunu yerel bilgisayardaki ekran kartımızla bedava ve ışık hızında çalıştıramaz mıyız?"* dedik ve kolları sıvadık:
1. `Microsoft.ML.OnnxRuntime.DirectML` ve `Microsoft.ML.Tokenizers` paketlerini yükledik.
2. Hugging Face'ten açık kaynaklı `ms-marco-MiniLM-L-6-v2.onnx` modelini ve `vocab.txt` WordPiece sözlüğünü `models/` klasörüne indirdik.
3. Kendi [`OnnxGpuReranker.cs`](../../src/TrustLab.Rag/Reranking/OnnxGpuReranker.cs) motorumuzu yazdık ve **DirectML üzerinden NVIDIA GeForce RTX 4060 Ti GPU'muza bağladık.**

**Canlı Test Çıktımız:**
```text
Evaluating Query : "penicillin allergy and amoxicillin contraindication"
[+] GPU Inference Latency: 281 ms
 Rank 1 | Score: 0.9845 | Type: ONNX_RTX4060Ti_GPU
   Chunk: Amoxicillin contraindication guidelines for severe penicillin hypersensitivity shock.
```
Ekran kartımız aradaki kahve tarifi gibi alakasız adayları gürültü filtresiyle eledi ve tıbbi uyarımızı **`0.9845` (%98.45)** ezici güven puanıyla zirveye oturttu!

---

## 🎯 Gün 2 Sonuç & "AI Doktorluğu" Vizyonu

Bugün Retrieval sürecini şu cümleyle zihnimize kazıdık:  
> *"Vektör arama konuyu anlar, BM25 kelimeyi yakalar, RRF ikisini birleştirir, GPU Cross-Encoder ise en kalitelisini zirveye fırlatıp çöpü eler."*

İlk defa RAG geliştiren biri olarak bugün sadece teoriyi öğrenmekle kalmadık; C# Clean Architecture altında **SIMD, BM25, RRF ve DirectML GPU** ile donanmış, 14/14 testi başarıyla geçen kurumsal bir arama omurgası kurduk.

Artık bozuk çalışan RAG sistemlerini iyileştiren gerçek bir **"AI Doktoru"** olma yolunda emin adımlarla ilerliyoruz! 🩺

**Gün 3'te:** Modelin halüsinasyon görmesini sıfırlayan **Deterministik Guardrail'ler, N-Gram Doğrulama, JSON Şema Otomatik Tamiri ve Devre Kesici (Circuit Breaker)** kalkanlarına gireceğiz! 🛡️

---
*İlgili Kod Dosyaları:*  
* [`Bm25SparseIndex.cs`](../../src/TrustLab.Rag/Indexing/Bm25SparseIndex.cs) • [`DenseVectorStore.cs`](../../src/TrustLab.Rag/Indexing/DenseVectorStore.cs) • [`ReciprocalRankFusion.cs`](../../src/TrustLab.Rag/Fusion/ReciprocalRankFusion.cs) • [`OnnxGpuReranker.cs`](../../src/TrustLab.Rag/Reranking/OnnxGpuReranker.cs) • [`HybridRetrievalPipeline.cs`](../../src/TrustLab.Rag/Pipeline/HybridRetrievalPipeline.cs)
