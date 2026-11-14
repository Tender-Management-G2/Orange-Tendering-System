using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SupplierInvitationDetailsViewModel
{
    public int Id { get; set; }
    public bool HasOffer { get; set; }
    public bool IsBiddingOpen => RoundStatus == TenderRoundStatus.Open && TenderStatus == TenderStatus.Bidding
        && Deadline.HasValue && Deadline.Value > DateTime.UtcNow
        && (!OpensAt.HasValue || OpensAt.Value <= DateTime.UtcNow);
    public bool WeightsApproved { get; set; }
    public bool CanCreateDraft => Status == InvitationStatus.Accepted && !HasOffer && IsBiddingOpen && WeightsApproved;
    public bool WaitingForWeights => Status == InvitationStatus.Accepted && !HasOffer && IsBiddingOpen && !WeightsApproved;
    public TenderRoundStatus RoundStatus { get; set; }
    public TenderStatus TenderStatus { get; set; }
    public bool CanRespond => Status == InvitationStatus.Pending && IsBiddingOpen;
    public string TenderTitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Requirements { get; set; }
    public int RoundNumber { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? Deadline { get; set; }
    public InvitationStatus Status { get; set; }
    public string DisplayStatus => Status == InvitationStatus.Pending && Deadline.HasValue && Deadline <= DateTime.UtcNow
        ? "Expired" : Status.ToString();
    public List<SupplierTenderItemViewModel> Items { get; set; } = new();
    public List<SupplierDocumentViewModel> Documents { get; set; } = new();
}

public sealed class SupplierTenderItemViewModel
{
    public string Name { get; set; } = string.Empty;
    public string? Specifications { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
}
