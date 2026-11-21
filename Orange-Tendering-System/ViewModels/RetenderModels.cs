using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.ViewModels;

// Officer's "Prepare the new round" after a Top Committee Re-Tender request (Services/TenderWorkflowService.TopRejection.cs).
public sealed record RetenderSupplier(int Id, string Name, string? Category, bool Previous);

public sealed class RetenderPlan
{
    public int TenderId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public int RoundNumber { get; set; }
    public int MaxRounds { get; set; }
    public string? Blocked { get; set; }
    public string? Reason { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime Deadline { get; set; }
    public List<RetenderSupplier> Suppliers { get; set; } = [];
}

public sealed class RetenderInput
{
    [Required(ErrorMessage = "Tender title is required."), MaxLength(200)] public string Title { get; set; } = "";
    [Required(ErrorMessage = "Tender description is required."), MaxLength(10000)] public string Description { get; set; } = "";
    [Required(ErrorMessage = "Choose the new bidding deadline.")] public DateTimeOffset Deadline { get; set; }
    [MinLength(3, ErrorMessage = "Invite at least 3 distinct suppliers.")] public List<int> SupplierIds { get; set; } = [];
}
