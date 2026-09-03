# 🛡️ TrustLab Mühendislik Günlüğü — Gün 6
**Tarih:** 2 Eylül 2026  
**Odak:** Canlı Korpus Testi (188 Chunk), Bidirectional Snap Doğrulaması, Akademik Citation Edge-Case'i, Layout-Aware Extraction ve İstatistiki Blok Koruması ☕

---

## ☕ Sabah Özeti: "Sistemi Canlıya Aldık ve 188 Chunk'la Yüzleştik"

Bu sabah TrustLab API'sini ve arayüzünü ayağa kaldırıp 4 gerçek kardiyoloji makalesinden üretilen **188 chunk'lık** korpus verisini doğrudan masaya yatırdık. 

Amacımız basitti: Dün yazdığımız **Bidirectional Snap-to-Boundary** mantığı sahada gerçekten çalıştı mı, yoksa sadece teoride mi kaldı?

---

## 🎯 1. Neyi Başardık? (Sözdizimsel Zafer)

188 chunk'ın tamamı tarandı:
* **0 Kırpık Kelime:** Eski `"Naive Overlap Drift"` faciasından eser yok. Hiçbir chunk `ullanmadan`, `ardiyak` ya da `tcomes` gibi yetim hecelerle başlamıyor.
* **Madde İmleri & Cümle Başları:** Madde imlerinde (`•`) ve tam cümle sonlarında (`.`, `!`) kilitlenme kusursuz çalıştı.

---

## ⚡ 2. Karşılaştığımız İlk Gerçek: "Chunk 1 ➔ Chunk 2 Citation İhlali"

Detaylı incelemede çok değerli bir **canlı prodüksiyon edge-case'i** yakaladık:

* **Chunk 1 Sonu:** `"...Cite this article as: Kahraman S, Demirci D, Demirci G, et al. Clinical Outcomes of"`
* **Chunk 2 Başı:** `"Clinical Outcomes of Double Kissing Culotte and Mini-Culotte Stenting..."`

### Neden Oldu?
Kelime bölünmedi ama **anlamsal tek bir alıntı (citation) dizesi ortadan ikiye ayrıldı.**  
Akademik makale başlıklarında ve yazar künyelerinde nokta (`.`) yerine virgül ve kısaltmalar (`et al.`, `Dr.`) kullanıldığı için algoritma cümle sonu bulamadı ve mecburen **kelime boşluğu fallback'ine** düştü.

---

## 💡 3. Mühendislik Kararı: Regex Tuzağı vs Layout-Aware Extraction

Bu problemi nasıl çözmeliyiz?

1. **Regex Tuzağına Düşmeme (Anti-Pattern):**  
   Her akademik derginin citation formatı farklıdır (Lancet, Nature, TKDA). Her format için ayrı regex yazmak projeyi sürdürülemez bir kural çöplüğüne çevirir.
2. **Kalıcı Çözüm (Layout-Aware Parsing):**  
   PDF'ten veri okurken **Bibliyografik Metadata (Yazar, Künye, DOI)** ile **Gövde Metnini (Abstract, Results, Discussion)** birbirinden ayırmak:
   * **İndeks Kirliliği Sıfırlanır:** Yazar adı veya adres bilgisi yüzünden BM25 indeksinde sahte eşleşmeler (false-positive) oluşmaz.
   * **UI Kanıt Kartı Korunur:** Kullanıcı arayüzünde kırık citation chunk'ı yerine tertemiz biçimlendirilmiş bir künye başlığı gösterilir.

---

## 🔬 4. İkinci Dalga Edge-Case'ler ve Klinik Bütünlük Zaferi

Layout-Aware yükleyiciyi canlıya aldığımızda bu kez daha derin ve klinik açıdan kritik 3 yeni edge-case tespit ettik:

### A) İstatistiki Ondalık Sayı ve Parantez İçi Bölünmesi (`P = 0.` ➔ `002]`)
* **Problem:** `SentenceDelimiters = ['.']` kuralı, `P = 0.002` veya `14.1%` içindeki ondalık noktasını cümle sonu zannedip istatistiksel değeri ortadan ikiye bölüyordu (`P = 0.` \| `002]`).
* **Çözüm (Numeric & Unclosed Bracket Guard):**
  1. Noktadan sonra rakam geliyorsa (`0.002`) veya `vs.` / `et al.` gibi bilinen kısaltmaysa cümle sonu sayılmaz.
  2. Henüz kapanmamış `[...]` veya `(...)` parantez blokları içindeki hiçbir nokta cümle sonu kabul edilmez; blok atomik olarak korunur.

### B) Tekil Bölüm Etiketleri (`ORIGINAL ARTICLE KLİNİK ÇALIŞMA`)
* **Problem:** Sayfa 1→2 geçişindeki tekil dergi fligranları, çok sayfalı tekrarlama eşiğine takılmadan gövde cümlesinin ortasına yapışıyordu.
* **Çözüm:** Bilinen dergi bölüm fligranları (`ORIGINAL ARTICLE`, `KLİNİK ÇALIŞMA`, `CASE REPORT` vb.) normalizasyon filtresiyle ayıklandı.

### C) Drop-Cap (Büyük Baş Harf) Boşluğu (`C oronary` ➔ `Coronary`)
* **Problem:** PDF katmanındaki dekoratif ilk harf ayrık metin bloğu olarak okunuyordu.
* **Çözüm:** `\b([A-Z])\s+([a-z]{2,})\b` örüntüsüyle otomatik birleştirildi.

---

## 🧪 5. Test Doğrulaması

Tüm regresyon senaryoları `LayoutAwarePdfLoaderTests.cs` altına eklendi:
* `PdfDocumentLoader_ExtractsPage1BibliographicMetadata_AndSeparatesFromChunkBody` ✅
* `PdfDocumentLoader_StripsRunningHeaders_FromIntermediatePages` ✅
* `Chunker_NeverSplits_InsideStatisticalBracketBlocks_OrOnDecimalPoints` ✅
* `Chunking_TKDA_Pdf_ProducesZeroMidCitationCutoffs_AndEnrichedMetadata` ✅

---

## 🎬 Günün Çıkarımı

> *"Klinik RAG sistemlerinde bir ondalık noktasının yanlış yerde bölünmesi, bir ilacın etkinliğinin `P = 0.002` yerine `P = 0.` görünmesine yol açar. Gerçek güvenilirlik, matematiksel ve istatistiksel ifadelerin atomik bütünlüğünü korumaktan geçer."*
