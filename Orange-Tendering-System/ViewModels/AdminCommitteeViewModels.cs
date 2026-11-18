using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class AdminCommitteeIndexViewModel
{
    public string? Search { get; set; }
    public List<AdminCommitteeTender> Tenders { get; set; } = [];
}
public sealed record AdminCommitteeTender(int Id, string Title, string Status, int EvaluationCount, int TopCount, bool HasCeo);

public sealed class AdminCommitteeManageViewModel
{
    public int TenderId { get; set; }
    public string TenderTitle { get; set; } = "";
    public string Status { get; set; } = "";
    public string Version { get; set; } = "";
    public bool EvaluationLocked { get; set; }
    public bool TopLocked { get; set; }
    public string? SourcingManagerId { get; set; }
    public string? TechnicalOneId { get; set; }
    public string? TechnicalTwoId { get; set; }
    public string? FinancialId { get; set; }
    public string? CeoId { get; set; }
    public string? TopMemberOneId { get; set; }
    public string? TopMemberTwoId { get; set; }
    public string? TopMemberThreeId { get; set; }
    public bool HasLegacyEvaluationSeat { get; set; }
    public List<SelectListItem> Managers { get; set; } = [];
    public List<SelectListItem> Evaluators { get; set; } = [];
    public List<SelectListItem> TopMembers { get; set; } = [];
    public List<AdminCommitteeSeat> CurrentSeats { get; set; } = [];
}
public sealed record AdminCommitteeSeat(string Committee, string Seat, string Name, string Email, bool Active);
public sealed record AdminCommitteeSeatInput(string Field, string Title, string Hint, string Role,
    string? SelectedId, List<SelectListItem> Options, string ReturnUrl, bool Locked);

public sealed class AdminCommitteeAccountsViewModel
{
    public string Committee { get; set; } = "";
    public List<AdminCommitteeAccountSlot> Slots { get; set; } = [];
}
public sealed record AdminCommitteeAccountSlot(Orange_Tendering_System.Models.CommitteeAccountSeat Seat, List<AdminCommitteeAccount> Accounts);
public sealed record AdminCommitteeAccount(string Id, string Name, string Email, bool Active);

public sealed class AdminCommitteeInput : IValidatableObject
{
    public int TenderId { get; set; }
    [Required] public string Version { get; set; } = "";
    public CommitteeType CommitteeType { get; set; }
    [Required] public string MemberOneId { get; set; } = "";
    [Required] public string MemberTwoId { get; set; } = "";
    [Required] public string MemberThreeId { get; set; } = "";
    [Required] public string MemberFourId { get; set; } = "";
    public string[] Members => [MemberOneId, MemberTwoId, MemberThreeId, MemberFourId];
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(CommitteeType)) yield return new("Choose a valid committee.");
        if (Members.Distinct(StringComparer.Ordinal).Count() != 4)
            yield return new("Choose four different members; a person cannot occupy two seats.");
    }
}
