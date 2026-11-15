using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SourcingOfficerDashboardViewModel
{
    public int SubmittedRequests { get; init; }
    public int AcceptedRequestsWithoutTender { get; init; }
    public int ActiveTenders { get; init; }
    public int OffersToReview { get; init; }
    public int ReportsToForward { get; init; }
    public PurchaseRequest? RequestToReview { get; init; }
    public Tender? TenderReadyForCommittee { get; init; }
    public string? DisplayName { get; init; }
}
