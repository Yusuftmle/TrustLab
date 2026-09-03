using System.Text.RegularExpressions;
using TrustLab.Application.Interfaces;
using TrustLab.Domain.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace TrustLab.Infrastructure.Documents;

/// <summary>
/// Evrensel ve Deterministik Layout-Aware PDF Yükleyici (Geometrical & Typographical Extraction):
/// 
/// 1. Bibliyografik Metadata Ayrıştırması: DOI, Citation, Yazar ve Dergi bilgilerini Document.Metadata'ya dinamik olarak taşır.
/// 2. Gövde Temizliği: 1. Sayfayı evrensel bölüm belirteçlerinden ("ABSTRACT / ÖZET / GİRİŞ / INTRODUCTION") başlatır.
/// 3. Dinamik Parmak İzi Running Header/Footer Temizliği: Sayfalar arası tekrarlayan satırları frekans ve konum analiziyle sıfır hardcode kural ile tespit edip temizler.
/// 4. Drop-Cap Normalizasyonu: Paragraf başındaki dekoratif büyük harf ayrışmalarını ("C oronary" -> "Coronary") genel regex kuralıyla birleştirir.
/// 5. Uluslararası Bölüm Etiketleri: "ORIGINAL ARTICLE", "KLİNİK ÇALIŞMA", "REVIEW" gibi tekil fligranları gövdeden ayıklar.
/// </summary>
public sealed class PdfDocumentLoader : IDocumentLoader
{
    private static readonly Regex DoiRegex = new(@"10\.\d{4,9}/[-._;()/:A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly Regex CitationRegex = new(@"Cite this article as:\s*(?<cite>[^\r\n]+(\r?\n(?!\s*(ABSTRACT|ÖZET|DOI|Received|Accepted|\d{4}))[^\r\n]+)*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PageNumberTrimmer = new(@"(^\s*\d+[\s\-\–]*|[\s\-\–]*\d+\s*$)", RegexOptions.Compiled);
    private static readonly Regex AbstractStartRegex = new(@"(^|\n|\r)\s*(ABSTRACT|ÖZET|SUMMARY|GİRİŞ|INTRODUCTION)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DropCapRegex = new(@"\b([A-Z])\s+([a-z]{2,})\b", RegexOptions.Compiled);
    private static readonly Regex SectionBadgesRegex = new(@"(^|\n|\r)\s*(ORIGINAL ARTICLE|KLİNİK ÇALIŞMA|KLINIK CALISMA|ORIGINAL INVESTIGATION|REVIEW ARTICLE|CASE REPORT|EDITORIAL|SPECIAL REPORT)\s*($|\n|\r)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InlineSectionBadgeRegex = new(@"\b(ORIGINAL ARTICLE|KLİNİK ÇALIŞMA|KLINIK CALISMA|ORIGINAL INVESTIGATION|REVIEW ARTICLE|CASE REPORT)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool CanHandle(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".pdf";
    }

    public Task<IReadOnlyList<Document>> LoadAsync(
        Stream stream,
        string fileName,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var documents = new List<Document>();

        try
        {
            var options = new ParsingOptions();
            if (!string.IsNullOrEmpty(password))
            {
                options.Password = password;
            }

            using var pdfDocument = PdfDocument.Open(stream, options);
            int totalPages = pdfDocument.NumberOfPages;

            if (totalPages == 0)
            {
                return Task.FromResult<IReadOnlyList<Document>>(documents);
            }

            // 1. ADIM: Sayfa Metinlerini Topla
            var rawPageTexts = new List<string>(totalPages);
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = pdfDocument.GetPage(pageNum);

                string pageText;
                try
                {
                    pageText = ContentOrderTextExtractor.GetText(page);
                }
                catch
                {
                    pageText = page.Text ?? string.Empty;
                }
                rawPageTexts.Add(pageText);
            }

            // 2. ADIM: 1. Sayfadan Bibliyografik Metadata Çıkarımı
            string page1Text = rawPageTexts.Count > 0 ? rawPageTexts[0] : string.Empty;
            var docMetadata = ExtractDocumentMetadata(page1Text, fileName, totalPages);

            // 3. ADIM: Sayfalar arası Running Header / Footer Dinamik Parmak İzi Tespiti (Frekans Analizi)
            var runningHeaderPatterns = DetectRunningHeaders(rawPageTexts);

            // 4. ADIM: Sayfa Metinlerini Temizleme ve Document Modellerine Dönüştürme
            for (int pageNum = 1; pageNum <= totalPages; pageNum++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string rawText = rawPageTexts[pageNum - 1];

                bool isImageOnly = string.IsNullOrWhiteSpace(rawText) || rawText.Trim().Length < 10;
                string cleanContent;

                if (isImageOnly)
                {
                    cleanContent = $"[Uyarı: {fileName} Sayfa {pageNum} metin katmanı içermiyor (taranmış resim olabilir).]";
                }
                else
                {
                    // Running header/footer satırlarını temizle (Evrensel frekans eşleştirmesi)
                    cleanContent = CleanRunningHeaders(rawText, runningHeaderPatterns);

                    // 1. Sayfada: Başlık ve Citation bloklarını gövdeden ayır, Abstract'tan başlat
                    if (pageNum == 1)
                    {
                        cleanContent = ExtractPage1CleanBody(cleanContent, docMetadata);
                    }

                    // Tekil bölüm etiketlerini (ORIGINAL ARTICLE, KLİNİK ÇALIŞMA vb.) temizle
                    cleanContent = CleanSectionBadges(cleanContent);

                    // Drop-Cap (Büyük Baş Harf) normalizasyonu ("C oronary" -> "Coronary")
                    cleanContent = NormalizeDropCaps(cleanContent);
                }

                var pageMeta = new Dictionary<string, string>(docMetadata)
                {
                    ["SourceFile"] = fileName,
                    ["PageNumber"] = pageNum.ToString(),
                    ["TotalPages"] = totalPages.ToString(),
                    ["FileType"] = "PDF",
                    ["IsScannedOrImageOnly"] = isImageOnly.ToString()
                };

                string docId = $"{fileName}#Sayfa_{pageNum}";
                documents.Add(Document.Create(docId, cleanContent.Trim(), pageMeta));
            }

            return Task.FromResult<IReadOnlyList<Document>>(documents);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new InvalidOperationException(
                $"PDF belgesi ({fileName}) şifre ile kilitlenmiş. Lütfen 'Parola' alanına geçerli şifreyi giriniz. Detay: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"PDF belgesi ({fileName}) işlenirken hata oluştu: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 1. Sayfa başlığından DOI, Citation, Yazar ve Dergi bilgilerini dinamik olarak çıkarır.
    /// </summary>
    private static Dictionary<string, string> ExtractDocumentMetadata(string page1Text, string fileName, int totalPages)
    {
        var meta = new Dictionary<string, string>
        {
            ["SourceFile"] = fileName,
            ["TotalPages"] = totalPages.ToString()
        };

        if (string.IsNullOrWhiteSpace(page1Text)) return meta;

        // DOI Tespiti
        var doiMatch = DoiRegex.Match(page1Text);
        if (doiMatch.Success)
        {
            meta["Doi"] = doiMatch.Value.TrimEnd('.', ';', ',');
        }

        // Citation Tespiti ("Cite this article as: ...")
        var citeMatch = CitationRegex.Match(page1Text);
        if (citeMatch.Success)
        {
            string citation = citeMatch.Groups["cite"].Value.Replace("\r\n", " ").Replace("\n", " ").Trim();
            citation = Regex.Replace(citation, @"\s+", " ");
            meta["Citation"] = citation;

            // Citation dizesinden Yazar ve Dergi parçalama:
            // Standart Format: [Yazarlar]. [Başlık]. [Dergi Adı]. [Yıl;Cilt(Sayı):Sayfalar]
            var parts = citation.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                meta["Authors"] = parts[0];
            }
            if (parts.Length >= 3)
            {
                meta["Journal"] = parts[2];
            }
            else if (parts.Length == 2 && parts[1].Length > 3)
            {
                meta["Journal"] = parts[1];
            }
        }

        // Dinamik Dergi Adı Çıkarımı Fallback'i (1. sayfanın en üst satırları taranır)
        if (!meta.ContainsKey("Journal"))
        {
            var topBanner = string.Join(" ", page1Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).Take(4));
            var journalMatch = Regex.Match(topBanner, @"(ARCHIVES OF [^.\n]+|JOURNAL OF [^.\n]+|ANATOLIAN JOURNAL OF [^.\n]+|TURKISH SOCIETY OF [^.\n]+|[A-Za-z\s]+ JOURNAL|[A-Za-z\s]+ ARŞİVİ)", RegexOptions.IgnoreCase);
            if (journalMatch.Success)
            {
                meta["Journal"] = Regex.Replace(journalMatch.Value, @"\s+", " ").Trim();
            }
        }

        return meta;
    }

    /// <summary>
    /// Sayfalar arasında (N >= 2) tekrarlayan üstbilgi/altbilgi (Running Header/Footer) parmak izlerini tespit eder.
    /// Sayfa numaralarını kırparak frekans analizi yapar (Tamamen Dergi/Format Bağımsızdır).
    /// </summary>
    private static HashSet<string> DetectRunningHeaders(List<string> rawPages)
    {
        var headerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in rawPages)
        {
            if (string.IsNullOrWhiteSpace(page)) continue;

            var lines = page.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) continue;

            // İlk 2 satıra (Header) ve Son 1 satıra (Footer) bak
            var candidateIndices = new List<int>();
            for (int i = 0; i < Math.Min(2, lines.Length); i++) candidateIndices.Add(i);
            if (lines.Length > 2) candidateIndices.Add(lines.Length - 1);

            foreach (int idx in candidateIndices)
            {
                string norm = NormalizeHeaderLine(lines[idx]);
                // En az 10 karakterlik anlamlı satırlar
                if (norm.Length >= 10)
                {
                    headerCounts[norm] = headerCounts.GetValueOrDefault(norm, 0) + 1;
                }
            }
        }

        // 2 veya daha fazla sayfada tekrarlayan parmak izleri running header/footer kabul edilir
        return headerCounts
            .Where(kvp => kvp.Value >= 2)
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeHeaderLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;
        string trimmed = PageNumberTrimmer.Replace(line.Trim(), "").Trim();
        return Regex.Replace(trimmed, @"\s+", " ");
    }

    /// <summary>
    /// Bir sayfa metninin başındaki ve sonundaki running header/footer satırlarını temizler.
    /// </summary>
    private static string CleanRunningHeaders(string pageText, HashSet<string> runningHeaders)
    {
        if (string.IsNullOrWhiteSpace(pageText) || runningHeaders.Count == 0)
        {
            return pageText;
        }

        var lines = pageText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
        if (lines.Count == 0) return pageText;

        // Baş kısımdaki (ilk 2 satır) tekrarlayan üstbilgileri kaldır
        int removeCount = 0;
        for (int i = 0; i < Math.Min(2, lines.Count); i++)
        {
            string norm = NormalizeHeaderLine(lines[i]);
            if (!string.IsNullOrWhiteSpace(norm))
            {
                // Parmak izi kümesinde tam veya kapsayan eşleşme kontrolü (Sıfır hardcode isim)
                bool isHeader = runningHeaders.Any(h =>
                    norm.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                    (norm.Length >= 15 && (norm.Contains(h, StringComparison.OrdinalIgnoreCase) || h.Contains(norm, StringComparison.OrdinalIgnoreCase))));

                if (isHeader)
                {
                    removeCount = i + 1;
                }
            }
        }

        if (removeCount > 0)
        {
            lines.RemoveRange(0, removeCount);
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 1. Sayfada bibliyografik header ve citation bloklarını ayıklayıp doğrudan gövdeye (Abstract/Giriş) odaklar.
    /// </summary>
    private static string ExtractPage1CleanBody(string pageText, Dictionary<string, string> metadata)
    {
        if (string.IsNullOrWhiteSpace(pageText)) return pageText;

        var match = AbstractStartRegex.Match(pageText);
        if (match.Success && match.Index > 0)
        {
            return pageText[match.Index..].Trim();
        }

        int citeIdx = pageText.IndexOf("Cite this article as:", StringComparison.OrdinalIgnoreCase);
        if (citeIdx >= 0)
        {
            int afterCite = pageText.IndexOf("\n", citeIdx);
            if (afterCite > 0 && afterCite < pageText.Length)
            {
                return pageText[afterCite..].Trim();
            }
        }

        return pageText;
    }

    /// <summary>
    /// Tekil bölüm başlıklarını ve etiketlerini (ORIGINAL ARTICLE, KLİNİK ÇALIŞMA vb.) temizler.
    /// </summary>
    private static string CleanSectionBadges(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        string cleaned = SectionBadgesRegex.Replace(text, "\n");
        cleaned = InlineSectionBadgeRegex.Replace(cleaned, "");
        return Regex.Replace(cleaned, @"[ \t]+", " ").Trim();
    }

    /// <summary>
    /// Paragraf başındaki Drop-Cap (Büyük Baş Harf) ayrışmalarını birleştirir.
    /// Örn: "C oronary" -> "Coronary", "P ericardial" -> "Pericardial"
    /// </summary>
    private static string NormalizeDropCaps(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return DropCapRegex.Replace(text, "$1$2");
    }
}
