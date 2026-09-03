# 📐 TrustLab Evrensel Doküman Ayrıştırma ve Semantik Chunking Standartları

**Versiyon:** 1.0 (Prodüksiyon Standardı)  
**Kapsam:** Akademik PDF'ler, Klinik Raporlar, Hukuki/Finansal Metinler ve Çok Sayfalı Dokümanlar  
**Temel Prensip:** Sıfır Hardcode, Yüksek Genellenebilirlik, Matematiksel/Algoritmik Koruma.

---

## 🎯 1. Neden Evrensel Standartlara İhtiyaç Var? (Anti-Pattern'ler)

Geleneksel RAG sistemlerinde yapılan en büyük 3 mühendislik hatası:
1. **Regex Tuzağı (Hardcoding Anti-Pattern):** Belirli bir makale veya yazar için kural yazmak (örn: `if (text.Contains("Kahraman"))`). Bu yaklaşım yüzlerce dergi içeren bir korpusta sürdürülemez.
2. **Kör Karakter Penceresi (Fixed-Length Window):** Metni körlemesine 1000 karaktere bölmek; kelimelerin ve istatistiki sayıların (`P = 0.` \| `002]`) ortadan ikiye ayrılmasına yol açar.
3. **Naive Overlap Drift:** Örtüşme (overlap) başlangıcını rastgele geri çekip yarım heceler (`ullanmadan`, `ardiyak`) üretmek.

---

## 🏛️ 2. Doküman Katmanı: Evrensel Layout-Aware Ayrıştırma Prensipleri

### A) Dinamik Çapraz Sayfa Parmak İzi Tespiti (Cross-Page Running Header/Footer Detection)
* **Geometrik Alan:** Sayfanın ilk 2 satırı (üstbilgi bandı) ve son 1 satırı (altbilgi bandı).
* **Sayfa Numarası Normalizasyonu:** Dinamik sayfa numaraları satır başı ve sonundan kırpılır:  
  $$\text{Normalize}(L) = \text{RegexReplace}(L, \verb@"(^\s*\d+[\s\-\–]*|[\s\-\–]*\d+\s*$)"@, \text{""})$$
* **Frekans Analizi:** Dokümanın $N \ge 2$ farklı sayfasında tekrarlayan normalize edilmiş parmak izleri **otomatik olarak running header/footer** kabul edilir ve sayfadan temizlenir.
* **Kazanım:** Makale adı veya dergi formatından bağımsız olarak üstbilgiler sıfır konfigürasyonla temizlenir.

### B) 1. Sayfa Bibliyografik Metadata İzolasyonu
* **Metadata Ayrımı:** DOI (`10.\d{4,9}/...`) ve Citation (`Cite this article as:`) blokları ayıklanıp `Document.Metadata` sözlüğüne yazılır.
* **Gövde Başlangıcı:** Gövde metni uluslararası standart bölüm belirteçlerinden (`ABSTRACT`, `ÖZET`, `INTRODUCTION`, `GİRİŞ`) başlatılır.
* **Kazanım:** Yazar kurum adresleri veya ISSN numaraları BM25 indeksini kirletmez (Zero Index Noise).

### C) Tipografik ve Karakter Normalizasyonu
* **Drop-Cap (Dekoratif Baş Harf) Birleştirme:**  
  $$\verb@\b([A-Z])\s+([a-z]{2,})\b \longrightarrow $1$2@$$  
  *Örn:* `"C oronary"` $\to$ `"Coronary"`, `"P ericardial"` $\to$ `"Pericardial"`.
* **Tekil Bölüm Fligranları:** `ORIGINAL ARTICLE`, `KLİNİK ÇALIŞMA`, `REVIEW ARTICLE`, `CASE REPORT` gibi tekil başlık şeritleri gövdeden arındırılır.

---

## 🔬 3. Chunker Katmanı: Deterministik Semantik Sınır (Bidirectional Snap-to-Boundary)

### A) Sayısal & Ondalık Değer Koruması (Numeric & Decimal Guard)
* Bir nokta (`.`) işaretinin solunda veya sağında rakam varsa (`0.002`, `14.1%`, `OR = 1.05`), bu nokta **cümle sonu değil, matematiksel bir ondalık ayracıdır**. Asla buradan bölünemez.

### B) Kapanmamış Parantez Kilidi (Unclosed Bracket Guard)
* Bir chunk kesme noktası aranırken açık parantez derinliği hesaplanır:
  $$\text{Depth} = \text{Count}(\verb@'['@) - \text{Count}(\verb@']'@) + \text{Count}(\verb@'('@) - \text{Count}(\verb@')'@)$$
* Eğer $\text{Depth} > 0$ ise, parantez kapanana kadar hiçbir noktalama işareti cümle sonu sayılamaz.  
* *Klinik Önem:* `[7 (7.6%) vs. 1 (0.9%), P = 0.017]` gibi istatistiki kanıt blokları **atomik olarak tek bir chunk içinde kalır**.

### C) Kısaltma Filtresi (Abbreviation Guard)
* Noktadan önceki kelime `vs.`, `et al.`, `Dr.`, `Fig.`, `Tab.`, `Ref.`, `No.` gibi bilinen bir kısaltmaysa cümle sonu sayılmaz.

### D) Çift Yönlü Kilitlenme (Bidirectional Snap)
1. **TargetEnd Snap:** Maksimum token sınırına gelindiğinde geriye doğru en yakın güvenli cümle sonuna kilitlenir; bulunamazsa parantez dışındaki en yakın kelime boşluğuna yaslanır.
2. **NextStart Snap (Overlap):** Örtüşme bölgesinde en yakın cümle başına kilitlenir; yoksa kelime başına atlar. Asla kelime ortasından başlamaz.

---

## 📊 4. Kalite Metrikleri ve Doğrulama

| Metrik | Beklenen Hedef |
| :--- | :--- |
| **Yarım Kelime (Half-word Token)** | **%0** |
| **Bölünmüş İstatistiki Değer (`P = 0.`)** | **%0** |
| **Mükerrer Sayfa Başlığı Sızıntısı** | **%0** |
| **BM25 False-Positive Yazar/Adres Eşleşmesi** | **%0** |
| **Tam Cümle & Paragraf Bütünlüğü** | **>%98** |
