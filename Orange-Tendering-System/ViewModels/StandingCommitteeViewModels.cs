using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.ViewModels;

public sealed class StandingCommitteeViewModel
{
    public string Committee { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Configured { get; set; }
    public List<StandingCommitteeSlot> Slots { get; set; } = [];
    public List<SavedCommitteeMember> SavedMembers { get; set; } = [];
}
public sealed record SavedCommitteeMember(string SeatKey, string Name, string Email, bool Active, bool RoleMatches);
public sealed record StandingCommitteeSlot(CommitteeAccountSeat Seat, string? SelectedId, List<SelectListItem> Candidates, bool PerTender);
public sealed class StandingCommitteeInput : IValidatableObject
{
    [Required] public string Committee { get; set; } = "";
    [Required] public string Version { get; set; } = "";
    [Required] public string MemberOneId { get; set; } = "";
    [Required] public string MemberTwoId { get; set; } = "";
    [Required] public string MemberThreeId { get; set; } = "";
    [Required] public string? MemberFourId { get; set; }
    public string[] FixedMembers => [MemberOneId, MemberTwoId, MemberThreeId, MemberFourId ?? ""];
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Committee is not ("Evaluation" or "Top")) yield return new("Choose a valid committee.");
        if (FixedMembers.Any(string.IsNullOrWhiteSpace) || FixedMembers.Distinct().Count() != FixedMembers.Length)
            yield return new("Complete all seats with different users. Each committee requires exactly four members.");
    }
}
