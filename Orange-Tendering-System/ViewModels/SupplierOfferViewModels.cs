using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SupplierOfferDetailsViewModel
{
    public int Id { get; set; }
    public int InvitationId { get; set; }
    public string TenderTitle { get; set; } = "";
    public int Revision { get; set; }
    public BidStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public bool CanEdit { get; set; }
    public bool CanContactOfficer => Status == BidStatus.Submitted;
    public string? Outcome { get; set; }
    public string? ResultMessage { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public SupplierDraftOfferInputModel Terms { get; set; } = new();
    public List<SupplierDocumentViewModel> Documents { get; set; } = new();
    public string? MissingRequirements { get; set; }
    public string ComplianceStatus { get; set; } = "";
    public decimal Total => Terms.Items.Sum(i => i.Quantity * i.UnitPrice)
        + Terms.ShippingCost + Terms.InstallationCost + Terms.MaintenanceCost + Terms.TaxAmount + Terms.OtherCost;
}

public sealed class SupplierDocumentViewModel
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string? DocumentType { get; set; }
    public DateTime UploadedAt { get; set; }
    public long SizeBytes { get; set; }
}

public sealed record SupplierHistoryRow(string Action, DateTime At);
