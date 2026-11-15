using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public partial class TenderWorkflowService
{
    private const string StandingClaim = "orange:standing-committee";
    private sealed record StandingRoster(string RoleId, string Version, Dictionary<string, string> Members);
    private async Task<StandingRoster> ReadStanding(string committee, bool locked = false)
    {
        Require(committee is "Evaluation" or "Top", "Invalid committee.");
        var name = committee == "Top" ? SystemRoles.TopCommittee : SystemRoles.EvaluationCommittee;
        var role = locked
            ? await db.Roles.FromSqlInterpolated($"SELECT * FROM AspNetRoles WITH (UPDLOCK,HOLDLOCK) WHERE Name={name}").SingleAsync()
            : await db.Roles.AsNoTracking().SingleAsync(x => x.Name == name);
        var records = await db.RoleClaims.AsNoTracking().Where(x => x.RoleId == role.Id && x.ClaimType == StandingClaim).OrderBy(x => x.Id).ToListAsync();
        Require(records.Count <= 1, "Committee configuration is inconsistent; contact technical support.");
        var json = records.SingleOrDefault()?.ClaimValue;
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json ?? "")));
        return new(role.Id, version, string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json)!);
    }
    private async Task ValidateStandingPerson(string id, string role)
    {
        var person = await users.FindByIdAsync(id);
        Require(person is { IsActive: true } && await Role(person, role), $"Choose an active account with the {role} role.");
        foreach (var permission in SystemRoles.AccountRoles(role))
            Require(await Role(person!, permission), "Save this account's job role in Users → Edit to synchronize its committee access before assigning it.");
        if (SystemRoles.AccountRoles(role).Contains(SystemRoles.TopCommittee))
            Require(!await Role(person!, SystemRoles.SourcingManager), "The Sourcing Manager cannot also occupy a Top Committee seat.");
        Require(!await Role(person!, SystemRoles.Admin) && !await Role(person!, SystemRoles.Supplier) && !await Role(person!, SystemRoles.SourcingOfficer), "Admin, Supplier and Sourcing Officer accounts cannot occupy committee seats.");
    }
    private static CommitteeAccountSeat[] FixedSeats(string committee) => CommitteeAccountSeats.All.Where(x => x.Committee == committee && x.Key != "evaluation-department").ToArray();
    private static CommitteeResponsibility ChiefSeat(string key) => key switch
    {
        "top-ceo" => CommitteeResponsibility.ChiefExecutiveOfficer,
        "top-cfo" => CommitteeResponsibility.ChiefFinancialOfficer,
        "top-cto" => CommitteeResponsibility.ChiefTechnologyOfficer,
        "top-coo" => CommitteeResponsibility.ChiefOperationsOfficer,
        _ => throw new WorkflowException("Unknown Chief seat.")
    };

    // Older tenders may not yet have a Top Committee. Seat the roster once, without changing any existing assignment.
    private async Task CaptureStandingTop(Tender tender, ApplicationUser actor)
    {
        var roster = await ReadStanding("Top", true);
        Require(roster.Members.Count == 4, "Admin must configure the four Chiefs before Top Committee review.");
        var existing = await db.CommitteeMembers.Where(x => x.TenderId == tender.Id).Select(x => x.UserId).ToListAsync();
        Require(roster.Members.Values.Distinct().Count() == 4 && !roster.Members.Values.Intersect(existing).Any(), "The Chiefs must be separate from this tender's Evaluation Committee.");
        foreach (var seat in FixedSeats("Top"))
        {
            Require(roster.Members.TryGetValue(seat.Key, out var id), "A Chief seat is missing.");
            await ValidateStandingPerson(id!, seat.Role);
            db.CommitteeMembers.Add(new() { TenderId = tender.Id, CommitteeType = CommitteeType.Top, Responsibility = ChiefSeat(seat.Key), UserId = id!, AssignedAt = Now });
        }
        Audit(actor, tender.Id, "FixedTopCommitteeApplied", roster.Members);
    }

    public async Task<StandingCommitteeViewModel> StandingCommittee(string committee, ClaimsPrincipal principal)
    {
        Require(await Role(await Actor(principal), SystemRoles.Admin), "Admin access is required.");
        var roster = await ReadStanding(committee);
        var slots = new List<StandingCommitteeSlot>();
        foreach (var seat in CommitteeAccountSeats.All.Where(x => x.Committee == committee))
        {
            var candidates = new List<SelectListItem>();
            foreach (var person in (await users.GetUsersInRoleAsync(seat.Role)).Where(x => x.IsActive).OrderBy(x => x.FullName))
            {
                if (await Role(person, SystemRoles.Admin) || await Role(person, SystemRoles.Supplier) || await Role(person, SystemRoles.SourcingOfficer)) continue;
                if (committee == "Top" && await Role(person, SystemRoles.SourcingManager)) continue;
                candidates.Add(new(person.FullName + " (" + person.Email + ")", person.Id));
            }
            slots.Add(new(seat, roster.Members.GetValueOrDefault(seat.Key), candidates, false));
        }
        var selectedIds = slots.Where(x => x.SelectedId != null).Select(x => x.SelectedId!).Distinct().ToList();
        var savedUsers = await db.Users.AsNoTracking().Where(x => selectedIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, x.Email, x.IsActive }).ToDictionaryAsync(x => x.Id);
        var savedMembers = new List<SavedCommitteeMember>();
        foreach (var slot in slots.Where(x => !x.PerTender && x.SelectedId != null))
            if (savedUsers.TryGetValue(slot.SelectedId!, out var person))
                savedMembers.Add(new(slot.Seat.Key, person.FullName, person.Email ?? "", person.IsActive, slot.Candidates.Any(x => x.Value == person.Id)));
        return new() { Committee = committee, Version = roster.Version, Configured = slots.Where(x => !x.PerTender).All(x => x.SelectedId != null && x.Candidates.Any(c => c.Value == x.SelectedId)), Slots = slots, SavedMembers = savedMembers };
    }

    public async Task SaveStandingCommittee(StandingCommitteeInput input, ClaimsPrincipal principal)
    {
        var actor = await Actor(principal);
        Require(await Role(actor, SystemRoles.Admin), "Admin access is required.");
        Require(Validator.TryValidateObject(input, new ValidationContext(input), [], true), "Complete the committee seats with different accounts.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        // Always lock the two permission rows in the same order to serialize roster changes with tender creation.
        var evaluation = await ReadStanding("Evaluation", true);
        var top = await ReadStanding("Top", true);
        var current = input.Committee == "Top" ? top : evaluation;
        var opposite = input.Committee == "Top" ? evaluation : top;
        Require(current.Version == input.Version, "This committee changed since the page was opened. Reload before saving.");
        Require(!input.FixedMembers.Intersect(opposite.Members.Values).Any(), "A person cannot belong to both committees.");
        var seats = CommitteeAccountSeats.All.Where(x => x.Committee == input.Committee).ToArray();
        var members = new Dictionary<string, string>();
        for (var i = 0; i < seats.Length; i++)
        {
            await ValidateStandingPerson(input.FixedMembers[i], seats[i].Role);
            members.Add(seats[i].Key, input.FixedMembers[i]);
        }
        var oldClaims = await db.RoleClaims.Where(x => x.RoleId == current.RoleId && x.ClaimType == StandingClaim).ToListAsync();
        db.RoleClaims.RemoveRange(oldClaims);
        db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = current.RoleId, ClaimType = StandingClaim, ClaimValue = JsonSerializer.Serialize(members) });
        db.AuditLogs.Add(new AuditLog { UserId = actor.Id, Action = "StandingCommitteeUpdated", EntityName = "Committee", EntityId = input.Committee,
            OldValues = JsonSerializer.Serialize(current.Members), NewValues = JsonSerializer.Serialize(members), CreatedAt = Now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    // Read by the existing tender dialog; empty entries are deliberately shown as unconfigured.
    public async Task<Dictionary<string, string>> StandingEvaluationDefaults() => (await ReadStanding("Evaluation")).Members
        .Where(x => x.Key != "evaluation-department").ToDictionary(x => x.Key, x => x.Value);

    private async Task<(Dictionary<string, string> Evaluation, Dictionary<string, string> Top)> RequiredStandingCommittees()
    {
        var evaluation = await ReadStanding("Evaluation", true);
        // The Admin roster also records its preferred Department Representative.
        // Preserve the existing workflow contract: other cycles still select that seat per tender.
        evaluation.Members.Remove("evaluation-department");
        var top = await ReadStanding("Top", true);
        foreach (var committee in new[] { "Evaluation", "Top" })
        {
            var roster = committee == "Top" ? top : evaluation;
            Require(roster.Members.Count == FixedSeats(committee).Length, $"Admin must configure the {committee} Committee before creating a tender.");
            foreach (var seat in FixedSeats(committee))
            {
                Require(roster.Members.TryGetValue(seat.Key, out var id), "A committee seat is missing.");
                await ValidateStandingPerson(id!, seat.Role);
            }
        }
        Require(evaluation.Members.Values.Concat(top.Members.Values).Distinct().Count() == 7, "The committees must contain seven different people; the eighth is the tender's department representative.");
        return (evaluation.Members, top.Members);
    }
}
