# 🛡️ TrustLab Mühendislik Günlüğü — Gün 1
**Tarih:** 17 Ağustos 2026  
**Odak:** RAG'ın En Kritik Adımı: Metin Parçalama (Chunking), Domain Tuzakları ve Doğru Strateji Seçimi

---

## ☕ Günün Başlangıcı: "Neden LangChain Varken Kendi Motorumuzu Yazıyoruz?"

Bugün çalışmaya başlarken hepimizin aklına gelen o çok doğal soruyu sorduk:  
*"Piyasada hazır kütüphaneler (LangChain, LlamaIndex vb.) varken neden oturup C# ile satır satır kendi `SemanticBoundaryChunker` motorumuzu yazdık?"*

Cevabı ararken fark ettik ki, internetteki RAG eğitimlerinin çoğu işin en hayati kısmını yüzeysel geçiyor: **Metin Parçalama (Chunking).**  
Hazır jenerik araçlar metinleri körlemesine karakter sınırından (`1000 karakter`) böldüğü için, iş gerçek hayata (özellikle Sağlık, Hukuk ve Finans gibi regüle alanlara) geldiğinde sistemler sessizce çuvallıyor.

---

## 🧠 Bugün Derinlemesine Öğrendiğimiz 4 Temel Mühendislik İlkesi

### 1. Temel Gerilim: "Arama Keskinliği" mi, "Bağlam Zenginliği" mi?
Metin parçalamada iki zıt kuvvetin savaştığını keşfettik:
* **Parçayı çok küçük yaparsak (50-100 token):** Arama motoru nokta atışı buluyor ama model bağlamı kaybediyor, cümlenin öncesini göremiyor.
* **Parçayı çok büyük yaparsak (1000+ token):** Model konuyu çok iyi anlıyor ama vektör uzayında anlam seyreliyor (**Vector Dilution**). Arama motoru samanlıkta iğne arar gibi şaşırıyor.
* **Altın Denge:** Endüstri standardı olan **256 - 512 token (~1000 - 2000 karakter)** bandının bir fikri eksiksiz anlatırken vektör keskinliğini korumak için en ideal nokta olduğunu belgeledik.

---

### 2. Tıp Alanındaki O Korkunç "Semantic Distance" Tuzağı
Bugünün en zihin açıcı tartışması şuydu: *"Madem öyle, cümleler arası kosinüs mesafesi aniden artınca bölen Anlamsal Sapma (Semantic Chunking) yöntemini tıpta kullanamaz mıyız?"*

Baktık ki orada çok tehlikeli bir tuzak var:
* **Cümle 1 (Dozaj):** *"Akut bakteriyel enfeksiyonda günde 2x1000 mg Amoksisilin oral yolla verilir."*
* **Cümle 2 (Ölümcül Uyarı):** *"Penisilin alerjisi olanlarda anaflaktik şok ve solunum durması gelişebilir."*

Embedding modeli bu iki cümlenin vektörünü "farklı konular" olarak görür (biri antibiyotik gramajı, diğeri ölümcül alerji şoku). İki cümle arasındaki kosinüs mesafesi aniden açıldığı için araya makası atar! Sonuç: İlaç dozu uyarısından kopar ve bir hastaya yanlış doz önerilerek **tıbbi malpraktis** doğar.

> **Çıkardığımız Ders:** Tıp ve Hukuk gibi alanlarda saf semantik sapmaya güvenilemez. **Yapısal Başlıklar (Protokoller) + Ebeveyn-Çocuk (Parent-Child)** hibriti kullanılmalıdır.

---

### 3. Ebeveyn-Çocuk (Parent-Child) Paradigması
Dünyada bu işin en ileri düzey standardını tartıştık:
* Aramayı **128 tokenlik küçük çocuk parçalarda** yap (arama motoru en keskin sonucu bulsun).
* Modele ise o küçük parçayı değil, onun bağlı olduğu **1000 tokenlik büyük ebeveyn paragrafı** ver (model tüm resmi ve uyarıları görsün).

---

### 4. Kendi Kodumuz: `SemanticBoundaryChunker.cs` Neyi Nasıl Çözdü?

Kendi geliştirdiğimiz [`SemanticBoundaryChunker.cs`](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs) sınıfını satır satır inceleyip şu mühendislik çözümlerini getirdiğimizi gördük:

* **Doğal Cümle Sınırları (`. ! ? \n`):** Parçaları kör bir karakter sayısıyla değil, cümlenin bittiği yerde kesiyoruz ([Satır 43-50](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs#L43-L50)).
* **100 Karakterlik Geriye Tarama (Lookback Heuristiği):** 1024. karakter kelimenin ortasına denk gelirse, geriye doğru en yakın noktayı bulup oradan bölüyoruz ([Satır 40-55](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs#L40-L55)).
* **Boşluk Fallback'i:** Nokta yoksa kelimeyi bölmemek için en yakın boşluğa (` `) yaslanıyoruz ([Satır 58-64](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs#L58-L64)).
* **Kayan Pencere Örtüşmesi (Sliding Window Overlap):** Yeni parçayı 32 token geriden başlatıyoruz. Böylece bir önceki parçadaki `"Ahmet Bey"` öznesi, yeni parçadaki `"O daha önce..."` zamiriyle bağını kaybetmiyor ([Satır 94](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs#L94)).
* **Kesin Karakter Koordinatları (`startOffset`, `endOffset`):** İleride arayüzde cevabın orijinal belgede nerede olduğunu sarı renkle parlatıp kaynak gösterebilmek (Citation) için harf koordinatlarını saklıyoruz ([Satır 81-82](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs#L81-L82)).

---

## 🎯 Gün 1 Sonuç & Yarının Heyecanı

Bugün metin parçalamanın (Chunking) sadece bir "string split" işlemi olmadığını; **dilbilgisi, istatistik, domain mantığı ve güvenlik optimizasyonunun** bir araya geldiği bir mühendislik sanatı olduğunu derinlemesine kavradık.

Parçalarımız tertemiz ve anlamsal olarak hazır.  
**Gün 2'de:** Bu parçaların 30 yıllık **Okapi BM25** leksikal arama ve donanım hızlandırmalı **SIMD Dense Vektör** depolarında nasıl indekslendiğine gireceğiz! 🚀

---
*İlgili Kod Dosyaları:*  
* [`SemanticBoundaryChunker.cs`](../../src/TrustLab.Rag/Chunking/SemanticBoundaryChunker.cs) • [`Chunk.cs`](../../src/TrustLab.Domain/Models/Chunk.cs) • [`Tokenizer.cs`](../../src/TrustLab.Domain/Common/Tokenizer.cs)
