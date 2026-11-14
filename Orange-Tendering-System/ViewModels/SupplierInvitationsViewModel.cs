using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SupplierInvitationsViewModel
{
    public string? Search { get; set; }
    public string? StatusFilter { get; set; }
    public string? AccessMessage { get; set; }
    public List<SupplierInvitationRowViewModel> Invitations { get; set; } = new();
}

public sealed class SupplierInvitationRowViewModel
{
    public int Id { get; set; }
    public string TenderTitle { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public DateTime InvitedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public InvitationStatus Status { get; set; }
    public string DisplayStatus => Status == InvitationStatus.Pending && Deadline.HasValue && Deadline <= DateTime.UtcNow
        ? "Expired" : Status.ToString();
}
