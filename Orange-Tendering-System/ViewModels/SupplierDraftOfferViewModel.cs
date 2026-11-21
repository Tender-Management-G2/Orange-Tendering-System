using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

// The supplier's offer page: tender facts are read-only and come from the server; only Input is posted.
public sealed class SupplierDraftOfferViewModel
{
    public int InvitationId { get; set; }
    public int? BidId { get; set; }
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public DateTime? Deadline { get; set; }
    public DateTime? NeededBy { get; set; }
    public string? RequiredCurrency { get; set; }
    public List<(string Label, string Value)> Requirements { get; set; } = [];
    public string[] RequiredDocuments { get; set; } = [];
    public List<SupplierDocumentViewModel> ExistingDocuments { get; set; } = [];
    public List<OfferItemContext> ItemContext { get; set; } = [];
    public SupplierDraftOfferInputModel Input { get; set; } = new();
    public List<string> Errors { get; set; } = [];
}

public sealed record OfferItemContext(int TenderItemId, string Name, string? Category, string? Specifications, decimal Quantity, string Unit);

// Supplier fields only; status, company, dates and totals are set by the server.
public sealed class SupplierDraftOfferInputModel
{
    [DataType(DataType.Date)]
    public DateTime? ValidUntil { get; set; }

    // Additional costs apply only when switched on; an unticked cost is stored as zero (already included or not applicable).
    public bool IncludeShipping { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal ShippingCost { get; set; }
    public bool IncludeInstallation { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal InstallationCost { get; set; }
    public bool IncludeMaintenance { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal MaintenanceCost { get; set; }
    [StringLength(1000)]
    public string? MaintenanceDescription { get; set; }
    public bool IncludeOther { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal OtherCost { get; set; }
    [StringLength(500)]
    public string? OtherCostDescription { get; set; }

    // "Rate": one tax rate (%) on the taxable total; "Amount": a stated total tax amount (mixed rates, exemptions).
    [RegularExpression("^(Rate|Amount)$")]
    public string TaxMode { get; set; } = "Rate";
    [Range(typeof(decimal), "0", "100")]
    public decimal? TaxRate { get; set; }
    public bool TaxIncludesCharges { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal TaxAmount { get; set; }

    [Required, RegularExpression("^(JOD|USD|EUR)$")]
    public string CurrencyCode { get; set; } = "JOD";

    [Range(1, 3650)]
    public int? DeliveryDays { get; set; }

    [StringLength(1000)]
    public string? PaymentTerms { get; set; }

    [StringLength(10000)]
    public string? TechnicalProposal { get; set; }

    [Range(0, 240)]
    public int? WarrantyMonths { get; set; }
    [StringLength(60)]
    public string? WarrantyType { get; set; }
    [StringLength(2000)]
    public string? WarrantyTerms { get; set; }
    [StringLength(4000)]
    public string? Deviations { get; set; }
    [StringLength(2000)]
    public string? AfterSalesSupport { get; set; }

    // Existing attachments of a draft the supplier removes before submitting.
    public List<int> RemoveDocumentIds { get; set; } = new();

    public List<SupplierDraftItemInputModel> Items { get; set; } = new();
}

public sealed class SupplierDraftItemInputModel
{
    public int TenderItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }

    [Range(typeof(decimal), "0.001", "999999999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "999999999")]
    public decimal UnitPrice { get; set; }

    [StringLength(300)]
    public string? OfferedProduct { get; set; }

    [StringLength(4000)]
    public string? OfferedSpecifications { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }
}

// Server-side financial summary of an offer. The browser shows the same arithmetic, but only this result is stored.
public sealed record OfferTotals(decimal ItemsSubtotal, decimal AdditionalCharges, decimal TotalBeforeTax, decimal Tax, decimal GrandTotal)
{
    public static OfferTotals Of(Bid bid) => Of(bid.Items.Sum(i => i.Quantity * i.UnitPrice), bid.ShippingCost + bid.InstallationCost + bid.MaintenanceCost + bid.OtherCost, bid.TaxAmount);
    public static OfferTotals Of(decimal items, decimal charges, decimal tax) => new(items, charges, items + charges, tax, items + charges + tax);
}
