using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

// Verified facts from the database about a supplier (never AI output, never invented performance scores).
public record SupplierRecord(int SupplierId, string Name, string Category, SupplierStatus Status, int VerifiedDocuments, int PendingDocuments,
    int PastSubmittedOffers, int PastAwards);

public record OfferLine(string Item, string? Category, decimal RequestedQuantity, string Unit, string? RequestedSpecifications,
    string? OfferedProduct, string? OfferedSpecifications, decimal OfferedQuantity, decimal UnitPrice, string? Notes)
{
    public decimal LineTotal => OfferedQuantity * UnitPrice;
}

public record OfferSummary(Bid Bid, SupplierRecord Supplier, List<OfferLine> Lines, List<Document> Documents)
{
    public decimal ItemsSubtotal => Lines.Sum(x => x.LineTotal);
    public decimal AdditionalCharges => Bid.ShippingCost + Bid.InstallationCost + Bid.MaintenanceCost + Bid.OtherCost;
    public decimal TotalBeforeTax => ItemsSubtotal + AdditionalCharges;
    public decimal GrandTotal => TotalBeforeTax + Bid.TaxAmount;
}

public class EvaluationWorkspaceModel
{
    public WorkflowDetails Details { get; set; } = null!;
    public PurchaseRequest Request { get; set; } = null!;
    public List<(string Label, string Value)> Requirements { get; set; } = [];
    public List<OfferSummary> Offers { get; set; } = [];
    public Dictionary<string, string> People { get; set; } = [];
    public EvaluationParticipation? Mine { get; set; }
    public CommitteeMember? MySeat { get; set; }
    public bool CanScore { get; set; }
    public bool BeforeStart { get; set; }
    public AiAnalysis? Ai { get; set; }
    public ReportReviewModel? Report { get; set; }
    public string[] RequiredDocuments { get; set; } = [];
    public decimal? Lowest => Offers.Count == 0 ? null : Offers.Min(x => x.GrandTotal);
}

// Offer document types a tender can require and a supplier can attach (stored on Document.DocumentType).
public static class OfferDocumentTypes
{
    public sealed record Kind(string Key, string Label, string Hint);
    public static readonly IReadOnlyList<Kind> All =
    [
        new("OfficialQuotation", "Official quotation", "Signed and stamped commercial quotation (PDF)"),
        new("TechnicalProposal", "Technical proposal", "Solution, implementation approach and compliance (PDF)"),
        new("ProductDatasheet", "Product datasheets", "Manufacturer specifications for offered items (PDF)"),
        new("WarrantyDocument", "Warranty document", "Warranty and after-sales conditions (PDF)"),
        new("Certificate", "Certificates", "Quality, authorization or conformity certificates (PDF/image)"),
        new("ReferenceImage", "Product images", "Photos of the offered products (PNG/JPEG); shown as supplier-provided"),
        new("OtherDocument", "Other documents", "Any other supporting document (PDF/image)")
    ];
    public static Kind? Find(string? key) => All.FirstOrDefault(x => x.Key == key);
    public static string Label(string? key) => Find(key)?.Label ?? (string.IsNullOrWhiteSpace(key) || key == "OfferAttachment" ? "Attachment" : key);
    public static string Label(string key, bool arabic) => !arabic ? Label(key) : key switch
    {
        "OfficialQuotation" => "عرض السعر الرسمي", "TechnicalProposal" => "العرض الفني",
        "ProductDatasheet" => "أوراق بيانات المنتجات", "WarrantyDocument" => "وثيقة الضمان",
        "Certificate" => "الشهادات", "ReferenceImage" => "صور المنتجات", "OtherDocument" => "وثائق أخرى",
        _ => Label(key)
    };
    public static string[] Parse(string? stored) => (stored ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(k => Find(k) != null).Distinct().ToArray();
}

// Money precision per currency: JOD has three decimals (fils); USD and EUR two.
public static class CurrencyRules
{
    public static readonly IReadOnlyList<string> Offer = ["JOD", "USD", "EUR"];
    public static readonly IReadOnlyList<string> Tender = ["JOD", "USD", "EUR", "SAR", "AED", "EGP"];
    public static int Decimals(string? currency) => currency == "JOD" ? 3 : 2;
    public static string Format(decimal value, string? currency) => value.ToString(Decimals(currency) == 3 ? "#,##0.000" : "#,##0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + currency;
}

public class PreTenderReviewModel
{
    public PreTenderReview? Current { get; set; }
    public List<PreTenderReview> History { get; set; } = [];
    public List<CommitteeMember> Members { get; set; } = [];
    public bool CanVote { get; set; }
    public bool CanSubmit { get; set; }
    public int? ApprovalsRequired { get; set; }
}

public class ExecutiveSummaryInput
{
    [Required, StringLength(4000, MinimumLength = 20)] public string BusinessNeed { get; set; } = "";
    [Required, StringLength(4000, MinimumLength = 20)] public string KeyFindings { get; set; } = "";
    [Required, StringLength(4000, MinimumLength = 20)] public string RecommendationReasons { get; set; } = "";
    [StringLength(4000)] public string? Risks { get; set; }
    [Required, StringLength(4000, MinimumLength = 20)] public string ManagerAssessment { get; set; } = "";
}
