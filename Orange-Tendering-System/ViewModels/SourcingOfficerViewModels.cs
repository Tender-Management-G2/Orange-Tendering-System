using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class CreateOfficerTenderModel
{
    public int PurchaseRequestId { get; set; }
    public string RequestTitle { get; set; } = "";
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required] public string Description { get; set; } = "";
    public string? Requirements { get; set; }
    [Required] public DateTime OpensAt { get; set; }
    [Required] public DateTime Deadline { get; set; }
    public List<PurchaseRequestItem> Items { get; set; } = [];
}

public sealed class SelectOfficerSuppliersModel
{
    public int RoundId { get; set; }
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = "";
    public string? SelectedCategory { get; set; }
    public bool CanInvite { get; set; }
    public string? InviteUnavailableReason { get; set; }
    public List<SupplierCategoryRowViewModel> Categories { get; set; } = [];
    public List<Orange_Tendering_System.Models.Entities.Supplier> Suppliers { get; set; } = [];
    public List<Orange_Tendering_System.Models.Entities.TenderInvitation> Invitations { get; set; } = [];
}

public sealed record SupplierCategoryRowViewModel(string Name, int SupplierCount);

public sealed class SourcingOfficerBidsViewModel
{
    public List<Bid> Offers { get; set; } = [];
    public List<OfferRoundSummaryViewModel> Rounds { get; set; } = [];
}

public sealed record OfferRoundSummaryViewModel(
    int RoundId, int TenderId, int RoundNumber, string TenderTitle,
    TenderRoundStatus Status, int CompliantOffers, int EvaluationMembers)
{
    public bool CanForward => Status == TenderRoundStatus.Open && CompliantOffers >= 3 && EvaluationMembers == 4;
}

public sealed class OfficerReportsViewModel
{
    public List<TenderReport> Reports { get; set; } = [];
    public HashSet<int> ForwardedReportIds { get; set; } = [];
}

public sealed class OfficerReportActionModel
{
    public int RoundId { get; set; }
    public string? Comment { get; set; }
    public List<int> BidIds { get; set; } = [];
    public List<Bid> Bids { get; set; } = [];
}
