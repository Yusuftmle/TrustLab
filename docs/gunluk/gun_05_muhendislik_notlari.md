# 🛡️ TrustLab Mühendislik Günlüğü — Gün 5
**Tarih:** 1 Eylül 2026  
**Odak:** Derin Kod İncelemesi (Deep Code Review), RAG & C# Terminoloji Analizi, "Deep Engineering" Felsefesi ve Kodun Reels Senaryosuna Dönüştürülmesi 🚀

---

## ☕ Günün Özeti: "Kod Yazmak Değil, Yazılan Kodun Ruhuna İnmek"

Bugün yeni bir kod satırı eklemek yerine, son 4 günde inşa ettiğimiz çekirdek mimariyi masaya yatırdık. Kodun her bir harfini, kullanılan C# anahtar kelimelerini (`sealed`, `record`, `enum`, `IReadOnlyList`), yabancı terminolojiyi ve arkasındaki üretim (production) risklerini adım adım analiz ettik. 

Aynı zamanda bu derin mühendislik birikimini yüzeysel "AI Slop" içeriklerinden ayıran **3Blue1Brown/Manim estetiğinde bir Reels senaryosuna** dönüştürdük.

---

## 🔍 1. Harf Harf Kod İncelemesi (Deep Code Review)

### A. [`SemanticBoundaryChunker.cs`](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs)
* **Neden `ITextChunker` Arayüzü?**  
  * *Dependency Inversion:* Sistemin geri kalanını (Pipeline, API) bozmadan yarın öbür gün `LlmBasedChunker` veya `FixedChunker` takabilmek için gevşek bağlılık (loose coupling) sağlandı.
* **`document.Content` Nereden Geliyor?**  
  * [`Document.cs`](../../src/TrustLab.Domain/Models/Document.cs) modelinden gelen ham metin gövdesidir.
* **Token $\to$ Karakter Çarpanı (`* 4`):**  
  * LLM dünyasındaki $1 \text{ Token} \approx 4 \text{ Karakter}$ kabulü üzerinden `maxChars = 1024` ve `overlapChars = 128` sınırları dinamik hesaplandı.
* **Cümle Sonu Kilidi (`SentenceDelimiters` & `boundaryLookback`):**  
  * 1024 karakterden geriye doğru son 120 karaktere bakılarak cümlenin ortasından değil, en yakın noktadan (`.`), ünlemden (`!`) veya satır başından (`\n`) kesim yapılması garanti altına alındı.

### B. [`GuardrailVerdict.cs`](../../src/TrustLab.Domain/Models/GuardrailVerdict.cs)
* **Neden `sealed record`?**  
  * Veri taşıma nesnelerinde (DTO) değişmezlik (immutability) ve yüksek bellek performansı sağlamak için `record` yapısı seçildi.
* **Kritik Hata Numaralandırmaları (`ValidationFailureReason`):**  
  * `UngroundedClaim`: Modelin kaynakta olmayan bir şeyi iddia etmesi.
  * `CircuitBreakerTripped`: Klinik dozaj hatası veya aşırı güvenlik ihlalinde sistemin sigortayı attırıp cevabı derhal bloke etmesi.

---

## 📚 2. Teknik Terimler ve Sektör Sözlüğü (Terminology)

| İngilizce Terim | Türkçe Karşılığı | Mühendislikteki Anlamı |
| :--- | :--- | :--- |
| **Chunk** | Parça / Lokma | Dökümanın vektör indeksine kaydedilen anlamsal bloğu. |
| **Boundary** | Sınır / Hudut | Cümlenin veya kelimenin doğal bitiş noktası. |
| **Delimiter** | Ayırıcı / Sınırlayıcı | Cümleleri bölen özel işaretler (`.`, `!`, `?`, `\n`, `•`). |
| **Overlap** | Örtüşme / Bindirme | Anlam kaybını önlemek için iki parça arasında paylaşılan tampon bölge. |
| **Offset** | Konum / İndeks Kayması | Metnin başından itibaren bulunulan karakter sayacı. |
| **Verdict** | Hüküm / Yargı | Guardrail hakeminin verdiği "Geçti (`Pass`)" veya "Kaldı (`Reject`)" kararı. |
| **Faithfulness** | Sadakat / Doğruluk | Model yanıtının kaynak dökümana sadakat oranı ($0.0 - 1.0$). |
| **Circuit Breaker** | Devre Kesici / Sigorta | Tehlikeli veya uydurma veri tespit edildiğinde üretimi durduran güvenlik duvarı. |

---

## 🧠 3. "Deep Engineering" vs "AI Slop" Felsefesi

Sektördeki en büyük yanılgı: *"Yapay zeka iki satırda kod yazıyor, mühendisliğe gerek kalmadı."*

* **Yüzeysel Yaklaşım (Demo Seviyesi):** `chunk_size=500` yazıp geçer. Kelimenin ortadan bölünüp `"s-Johnson"` veya `"ullanmadan"` haline geldiğini fark etmez. Sistem canlıda patladığında prompt düzeltmeye çalışır.
* **Derin Mühendislik (Production Seviyesi):** Karakter ve byte offset'ine iner, *Bidirectional Snap-to-Boundary* algoritması yazar, n-gram grounding ve Regex tabanlı klinik devre kesicilerle sistemi güvenceye alır.

---

## 🎬 4. Kodun Reels Senaryosuna Dönüştürülmesi

TrustLab'de çözdüğümüz problemleri ve mimariyi Manim dikey video kurgusuyla eşleştirdik:

1. **00:00 - 00:07 (Kanca):** $4.7M'lık Dozaj Bölünme Felaketi $\to$ [`SemanticBoundaryChunker.cs`](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs) *Naive Overlap Drift* bug'ı.
2. **00:07 - 00:18 (Paradoks):** 2048T Blok vs 32T Mikro Cümle Terazisi $\to$ Vektör hassasiyeti ve gürültü dengesi.
3. **00:18 - 00:32 (Çözüm):** "Search Small, Retrieve Big" $\to$ `source_doc_id` ve child chunk araması üzerinden parent bağlam genişletmesi.
4. **00:32 - 00:42 (Mühendislik Sınırı):** Re-ranking & Cümle Doğrulaması $\to$ [`CrossEncoderReranker.cs`](../../src/TrustLab.Rag/Reranking/CrossEncoderReranker.cs) & [`DosageAndNumericGuard.cs`](../../src/TrustLab.Guardrails/Grounding/DosageAndNumericGuard.cs).

---

## 🏁 Günün Çıkarımı
En iyi kod, sadece yazılan kod değil; **her bir satırının neden orada olduğunu bildiğin ve savunabildiğin koddur.** Bugün sistemin temellerini zihnimize ve dokümantasyonumuza kazıdık.
