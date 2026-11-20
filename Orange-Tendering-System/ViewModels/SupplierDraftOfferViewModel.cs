using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Orange_Tendering_System.ViewModels;

public sealed class SupplierDraftOfferViewModel
{
    public int InvitationId { get; set; }
    public int? BidId { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public DateTime? Deadline { get; set; }
    public SupplierDraftOfferInputModel Input { get; set; } = new();
    public List<SupplierDocumentViewModel> Documents { get; set; } = new();
}

// نستقبل حقول المورد فقط؛ الحالة والشركة والتاريخ يحددها السيرفر.
public sealed class SupplierDraftOfferInputModel
{
    [DataType(DataType.Date)]
    public DateTime? ValidUntil { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal ShippingCost { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal InstallationCost { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal MaintenanceCost { get; set; }
    [BindNever]
    public decimal TaxAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999")]
    public decimal OtherCost { get; set; }
    [Required, RegularExpression("^(JOD|USD|EUR)$")]
    public string CurrencyCode { get; set; } = "JOD";

    [Range(1, 3650)]
    public int? DeliveryDays { get; set; }

    [StringLength(1000)]
    public string? PaymentTerms { get; set; }

    [StringLength(10000)]
    public string? TechnicalProposal { get; set; }

    public List<SupplierDraftItemInputModel> Items { get; set; } = new();

    public OfferPriceBreakdown PriceBreakdown(bool live = false) => new(CurrencyCode, Items.Sum(item => item.Quantity * item.UnitPrice),
        ShippingCost, InstallationCost, MaintenanceCost, OtherCost, TaxAmount, live);
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

    [StringLength(4000)]
    public string? OfferedSpecifications { get; set; }
}
