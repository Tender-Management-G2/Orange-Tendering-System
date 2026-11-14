using Orange_Tendering_System.Models.Entities;
namespace Orange_Tendering_System.ViewModels;

// Supplier response contract: no committee, weights, scores, recommendation, competitors or internal stages.
public sealed class SupplierTenderPortalViewModel
{
    public int TenderId { get; init; }
    public int InvitationId { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Requirements { get; init; }
    public int RoundNumber { get; init; }
    public DateTime? OpensAt { get; init; }
    public DateTime? Deadline { get; init; }
    public string Availability { get; init; } = "Closed";
    public InvitationStatus InvitationStatus { get; init; }
    public bool CanRespond { get; init; }
    public bool CanCreateOffer { get; init; }
    public List<SupplierTenderItemViewModel> Items { get; init; } = [];
    public List<SupplierDocumentViewModel> Documents { get; init; } = [];
    public List<SupplierPortalOffer> Offers { get; init; } = [];
}
public sealed record SupplierPortalOffer(int Id, string Status, bool CanEdit);
