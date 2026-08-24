# 🛡️ TrustLab Mühendislik Günlüğü — Gün 3
**Tarih:** 24 Ağustos 2026  
**Odak:** Deterministik Guardrail'ler, Dozaj Kilidi, RAG Triad Olgusallığı, Perplexity Seviyesi İnteraktif Kaynak Atıfı ve Canlı Donanım Gözlemlenebilirliği 🚀

---

## ☕ Günün Başlangıcı: "Model Yanıt Üretiyor Ama Doğru mu Söylüyor?" Sorunu

Dün güçlü bir hibrit arama (BM25 + Dense + GPU Re-Ranker) hattı kurmuştuk. Ancak bugün LLM'i (`qwen2.5:7b`) hatta bağladığımızda klinik yapay zekanın en büyük krizini masaya yatırdık:  
*"Model prospektüsü okuyor ama bazen kafasına göre dozaj ekliyor veya belgede yazmayan genel yorumlar katıyor. Bir doktor veya hasta bu yanıta nasıl %100 güvenebilir?"*

Çözümümüz; modele körü körüne güvenmek yerine, üretilen **her bir cümleyi ve her bir dozu matematiksel olarak denetleyen deterministik bir güvenlik ve gözlemlenebilirlik zırhı** örmek oldu.

---

## 🧠 Bugün Yaşadığımız ve Çözdüğümüz 4 Büyük Mühendislik Aşaması

### 1. Hardcoded Kurallardan Saf GPU Semantik Yönlendirmeye Geçiş
İlk başta genel sohbet ve selamlaşmaları ("naber", "merhaba") yakalamak için string listeleri kullanmanın kırılgan ve sürdürülemez olduğunu fark ettik.
* **Yaptığımız Çözüm:** Hardcoded listelerin tamamını çöpe attık.
* Sorunun dokümanla alakasını doğrudan **RTX 4060 Ti üzerindeki ONNX Cross-Encoder** modelinin ürettiği float güven skoruna (`maxGpuScore`) devrettik.
* Soru tıbbi bir içerik taşıyorsa (`Skor > 0.05`), sistem doğrudan **Klinik RAG ve Kanıt Doğrulama** moduna geçer.
* Soru genel bir selamlama ise (`Skor ≈ 0.00`), sistem halüsinasyon alarmı vermeden doğrudan profesyonel **Klinik Karar Destek Asistanı** kimliğiyle nazik bir diyalog kurar.

> **Kodumuz:** [`Program.cs`](../../src/TrustLab.Api/Program.cs#L613-L655)

---

### 2. Parçalanmış Satırlardan Bütünsel Paragraf Chunking'e (256-Token Sliding Window)
PDF sisteme yüklenirken her satır sonu (`\n`) ayrı bir doküman parçası yapıldığında büyük bir problemle karşılaştık:
* *"1. PAROL nedir ve ne için kullanılır?"* başlığı tek bir satırda kalıyor, altındaki açıklama paragrafı diğer parçaya kayıyordu. Arama motoru başlığı bulsa da modele altındaki paragraf gitmediği için model eksik bilgiyle yanıltıcı yanıtlar üretiyordu.
* **Çözümümüz:** Satır bazlı bölme yerine **256 token uzunluğunda ve 64 token örtüşmeli (overlap)** kayan pencere (sliding window) chunking mimarisine geçtik. Başlıklar ve altındaki klinik açıklamalar tek bir zengin bağlam parçası olarak korundu.

> **Kodumuz:** [`Program.cs`](../../src/TrustLab.Api/Program.cs#L573-L595)

---

### 3. Sıfır-Tolerans: Deterministik Dozaj ve Sayısal Güvenlik Kilidi (Dosage Guard)
Tıbbi metinlerde en ufak sayısal hata ölümcül olabilir (`500 mg` yerine `5000 mg` veya önerilmeyen tablet sayısı gibi).
* Modelin ürettiği metindeki tüm sayısal iddiaları (`mg`, `tablet`, `günlük doz`) Regex ve token analiziyle çıkaran bir denetçi motor kurduk.
* **Canlı Güvenlik Testi:** Kullanıcı *"Ben 12 tane alsam ne olur?"* (ölümcül aşırı doz) diye sorduğunda, modelin belgede olmayan spekülatif cümlelerini sistem anında yakaladı:
  > `"Kadınlar için dozaj..."` ➔ **`[⚠️ Uydurma]`**  
  > Asistan Başlığı ➔ **`⚠️ GÜVENLİK KİLİDİ DEVREDE`**
* Bu sayede model uydurmaya kalktığında sistem cevabı mühürleyip kırmızı alarm veriyor.

> **Kodumuz:** [`DosageAndNumericGuard.cs`](../../src/TrustLab.Guardrails/Grounding/DosageAndNumericGuard.cs)

---

### 4. Perplexity Seviyesinde İnteraktif Canlı Kanıt & PDF Satır Bulucu Modal 📑
Gözlemlenebilirliği sadece rakamlardan ibaret bırakmayıp görsel bir şölene dönüştürdük:
1. **Cümle Bazlı Sadakat Rozetleri:** Üretilen her cümlenin yanına matematiksel destek oranı eklendi (`[✅ %100]`, `[✅ %85]`, `[⚠️ Uydurma]`).
2. **Canlı Kanıt Parlatıcısı:** Yanıttaki herhangi bir cümlenin üzerine gelindiğinde, sağ panelde o cümlenin PDF'teki tam satır numarası (`📍 Satır #149`) ve birebir kaynak paragrafı parlar.
3. **Tek Tıkla PDF İnceleyicisi (Auto-Scroll):** Cümleye tıklandığında **Kaynak PDF Doküman İnceleyicisi** açılır; 500 satırlık doküman içinde otomatik olarak o satıra kayar (`smooth scroll`) ve ilgili satırı **altın sarısı renkle (`active-highlight-line`)** vurgular.

> **Kodumuz:** [`ui/app.js`](../../ui/app.js) • [`ui/style.css`](../../ui/style.css) • [`RagTriadEvaluator.cs`](../../src/TrustLab.Guardrails/Evaluation/RagTriadEvaluator.cs)

---

## 📊 Güncel Donanım ve Pipeline Özeti

| Katman | Teknoloji / Model | Rol & Performans |
| :--- | :--- | :--- |
| **Backend Çekirdeği** | .NET 10 C# Minimal API | Clean Architecture, Mikrosaniye gecikme |
| **GPU Re-Ranker** | `ms-marco-MiniLM-L-6-v2.onnx` (DirectML) | **NVIDIA RTX 4060 Ti (220 ms GPU süresi)** |
| **Yerel LLM** | Ollama `qwen2.5:7b` (4.68 GB) | 29/29 Katman VRAM'de, 0 API maliyeti |
| **Arama Füzyonu** | Okapi BM25 + SIMD Dense ($K=60$ RRF) | Leksikal + Anlamsal Çift Katman |
| **Güvenlik & SLA** | RAG Triad + N-Gram + Dozaj Kilidi | Deterministik Halüsinasyon Engelleyici |

---

## 🎯 Gün 3 Sonuç

Bugün sadece soruya cevap veren bir chatbot değil; **ürettiği her kelimenin ve her miligramın hesabını PDF'in satır numarasına kadar verebilen, halüsinasyon anında kendini kilitleyen kurumsal düzeyde bir Klinik Güvenlik & Gözlemlenebilirlik Platformu** inşa ettik.

*İlgili Kod Dosyaları:*  
* [`Program.cs`](../../src/TrustLab.Api/Program.cs) • [`DosageAndNumericGuard.cs`](../../src/TrustLab.Guardrails/Grounding/DosageAndNumericGuard.cs) • [`RagTriadEvaluator.cs`](../../src/TrustLab.Guardrails/Evaluation/RagTriadEvaluator.cs) • [`NgramGroundingGuard.cs`](../../src/TrustLab.Guardrails/Grounding/NgramGroundingGuard.cs) • [`ui/app.js`](../../ui/app.js) • [`ui/index.html`](../../ui/index.html)
