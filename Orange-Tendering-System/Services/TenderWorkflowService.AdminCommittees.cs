using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public partial class TenderWorkflowService
{
    public async Task<AdminCommitteeAccountsViewModel> AdminCommitteeAccounts(string committee, ClaimsPrincipal principal)
    {
        Require(await Role(await Actor(principal), SystemRoles.Admin), "Admin access is required.");
        Require(committee is "Evaluation" or "Top", "Choose a valid committee.");
        var seats = CommitteeAccountSeats.All.Where(x => x.Committee == committee).ToList();
        var keys = seats.Select(x => x.Key).ToList();
        var records = await (from claim in db.UserClaims.AsNoTracking()
                             join person in db.Users.AsNoTracking() on claim.UserId equals person.Id
                             join userRole in db.UserRoles on person.Id equals userRole.UserId
                             join role in db.Roles on userRole.RoleId equals role.Id
                             where claim.ClaimType == CommitteeAccountSeats.ClaimType && keys.Contains(claim.ClaimValue!)
                             select new { claim.ClaimValue, person.Id, person.FullName, person.Email, person.IsActive, role.Name })
                            .ToListAsync();
        return new() { Committee = committee, Slots = seats.Select(seat => new AdminCommitteeAccountSlot(seat,
            records.Where(x => x.ClaimValue == seat.Key && x.Name == seat.Role).DistinctBy(x => x.Id).OrderBy(x => x.FullName)
                .Select(x => new AdminCommitteeAccount(x.Id, x.FullName, x.Email ?? "", x.IsActive)).ToList())).ToList() };
    }

    private static string CommitteeVersion(IEnumerable<CommitteeMember> members) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("|", members.OrderBy(x => x.Id)
            .Select(x => $"{x.Id}:{x.UserId}:{x.CommitteeType}:{x.Responsibility}")))));

    private async Task<bool> EvaluationMembershipLocked(int id) =>
        await db.Set<WeightRound>().AnyAsync(x => x.TenderId == id) ||
        await db.CommitteeRounds.AnyAsync(x => x.TenderRound.TenderId == id && x.CommitteeType == CommitteeType.Evaluation) ||
        await db.EvaluationScores.AnyAsync(x => x.CommitteeMember.TenderId == id);

    private async Task<bool> TopMembershipLocked(int id) =>
        await db.CommitteeVotes.AnyAsync(x => x.CommitteeMember.TenderId == id && x.CommitteeMember.CommitteeType == CommitteeType.Top) ||
        await db.Set<ReportSignature>().AnyAsync(x => x.TenderReport.TenderRound.TenderId == id && x.Stage == ReportSigningStage.Top);

    public async Task<AdminCommitteeIndexViewModel> AdminCommittees(string? search, ClaimsPrincipal principal)
    {
        Require(await Role(await Actor(principal), SystemRoles.Admin), "Admin access is required.");
        var query = db.Tenders.AsNoTracking();
        search = search?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = int.TryParse(search, out var id) ? query.Where(x => x.Id == id || x.Title.Contains(search)) : query.Where(x => x.Title.Contains(search));
        var items = await query.OrderByDescending(x => x.Id).Select(x => new
        {
            x.Id, x.Title, x.Status,
            Evaluation = x.CommitteeMembers.Count(m => m.CommitteeType == CommitteeType.Evaluation),
            Top = x.CommitteeMembers.Count(m => m.CommitteeType == CommitteeType.Top),
            Ceo = x.CommitteeMembers.Any(m => m.CommitteeType == CommitteeType.Top && m.Responsibility == CommitteeResponsibility.ChiefExecutiveOfficer)
        }).ToListAsync();
        return new() { Search = search, Tenders = items.Select(x => new AdminCommitteeTender(x.Id, x.Title, x.Status.ToString(), x.Evaluation, x.Top, x.Ceo)).ToList() };
    }

    public async Task<AdminCommitteeManageViewModel?> AdminCommitteeDetails(int id, ClaimsPrincipal principal)
    {
        Require(await Role(await Actor(principal), SystemRoles.Admin), "Admin access is required.");
        var tender = await db.Tenders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (tender == null) return null;
        var members = await db.CommitteeMembers.AsNoTracking().Include(x => x.User).Where(x => x.TenderId == id).OrderBy(x => x.Id).ToListAsync();
        bool closed = tender.Status is TenderStatus.Awarded or TenderStatus.Rejected or TenderStatus.Cancelled;
        var evaluation = members.Where(x => x.CommitteeType == CommitteeType.Evaluation).ToList();
        var top = members.Where(x => x.CommitteeType == CommitteeType.Top).ToList();
        string? Seat(CommitteeResponsibility r) => evaluation.SingleOrDefault(x => x.Responsibility == r)?.UserId;
        var others = top.Where(x => x.Responsibility != CommitteeResponsibility.ChiefExecutiveOfficer).ToList();
        async Task<List<SelectListItem>> Candidates(string role, CommitteeType type)
        {
            var result = new List<SelectListItem>();
            foreach (var person in (await users.GetUsersInRoleAsync(role)).Where(x => x.IsActive).OrderBy(x => x.FullName))
            {
                if (person.Id == tender.CreatedByUserId || await Role(person, SystemRoles.Admin) || await Role(person, SystemRoles.Supplier) || await Role(person, SystemRoles.SourcingOfficer)) continue;
                if (type == CommitteeType.Top && await Role(person, SystemRoles.SourcingManager)) continue;
                if (members.Any(x => x.CommitteeType != type && x.UserId == person.Id)) continue;
                result.Add(new(person.FullName + " (" + person.Email + ")", person.Id));
            }
            return result;
        }
        return new()
        {
            TenderId = id, TenderTitle = tender.Title, Status = tender.Status.ToString(), Version = CommitteeVersion(members),
            EvaluationLocked = closed || tender.Status != TenderStatus.Draft || await EvaluationMembershipLocked(id),
            TopLocked = closed || await TopMembershipLocked(id),
            SourcingManagerId = Seat(CommitteeResponsibility.SourcingManager),
            TechnicalOneId = Seat(CommitteeResponsibility.TechnicalRepresentative),
            TechnicalTwoId = Seat(CommitteeResponsibility.TechnicalRepresentative2),
            FinancialId = Seat(CommitteeResponsibility.FinancialRepresentative),
            CeoId = top.SingleOrDefault(x => x.Responsibility == CommitteeResponsibility.ChiefExecutiveOfficer)?.UserId,
            TopMemberOneId = others.ElementAtOrDefault(0)?.UserId,
            TopMemberTwoId = others.ElementAtOrDefault(1)?.UserId,
            TopMemberThreeId = others.ElementAtOrDefault(2)?.UserId,
            HasLegacyEvaluationSeat = evaluation.Any(x => x.Responsibility == CommitteeResponsibility.DepartmentRepresentative),
            Managers = await Candidates(SystemRoles.SourcingManager, CommitteeType.Evaluation),
            Evaluators = await Candidates(SystemRoles.EvaluationCommittee, CommitteeType.Evaluation),
            TopMembers = await Candidates(SystemRoles.TopCommittee, CommitteeType.Top),
            CurrentSeats = members.Select(x => new AdminCommitteeSeat(x.CommitteeType.ToString(), x.Responsibility switch
            {
                CommitteeResponsibility.SourcingManager => "Sourcing Manager",
                CommitteeResponsibility.FinancialRepresentative => "Financial Representative",
                CommitteeResponsibility.TechnicalRepresentative => "Technical Member 1",
                CommitteeResponsibility.TechnicalRepresentative2 => "Technical Member 2",
                CommitteeResponsibility.ChiefExecutiveOfficer => "CEO",
                CommitteeResponsibility.DepartmentRepresentative => "Department Representative (legacy)",
                _ => "Committee Member"
            }, x.User.FullName, x.User.Email ?? "", x.User.IsActive)).ToList()
        };
    }

    public Task SaveAdminCommittee(AdminCommitteeInput input, ClaimsPrincipal principal) => Change(input.TenderId, principal, "AdminCommitteeAssigned", async (t, actor) =>
    {
        Require(await Role(actor, SystemRoles.Admin), "Admin access is required.");
        Require(!await db.AuditLogs.AnyAsync(x => x.EntityName == "Tender" && x.EntityId == t.Id.ToString() && x.Action == "FixedCommitteesApplied"), "This tender uses the committees saved by Admin. Its frozen membership cannot be replaced here.");
        Require(Validator.TryValidateObject(input, new ValidationContext(input), [], true), "Select four distinct members and reload the page if needed.");
        Require(t.Status is not (TenderStatus.Awarded or TenderStatus.Rejected or TenderStatus.Cancelled), "Closed tenders cannot be changed.");
        var existing = await db.CommitteeMembers.Where(x => x.TenderId == t.Id).ToListAsync();
        Require(CommitteeVersion(existing) == input.Version, "Committee membership changed since you opened this page. Reload and review the current members.");
        Require(input.CommitteeType != CommitteeType.Evaluation || !await IsLowestPrice(t), "Financial-only tenders have no Evaluation Committee.");
        Require(input.CommitteeType == CommitteeType.Evaluation
            ? t.Status == TenderStatus.Draft && !await EvaluationMembershipLocked(t.Id)
            : !await TopMembershipLocked(t.Id), "Membership is locked because this committee's work has already started.");
        var old = existing.Where(x => x.CommitteeType == input.CommitteeType).ToList();
        var opposite = existing.Where(x => x.CommitteeType != input.CommitteeType).Select(x => x.UserId).ToHashSet();
        var seats = input.CommitteeType == CommitteeType.Evaluation
            ? new CommitteeResponsibility?[] { CommitteeResponsibility.SourcingManager, CommitteeResponsibility.TechnicalRepresentative, CommitteeResponsibility.TechnicalRepresentative2, CommitteeResponsibility.FinancialRepresentative }
            : [CommitteeResponsibility.ChiefExecutiveOfficer, null, null, null];
        for (var i = 0; i < input.Members.Length; i++)
        {
            var person = await users.FindByIdAsync(input.Members[i]);
            Require(person is { IsActive: true }, "Every selected member must have an active account.");
            var role = input.CommitteeType == CommitteeType.Top ? SystemRoles.TopCommittee : i == 0 ? SystemRoles.SourcingManager : SystemRoles.EvaluationCommittee;
            Require(await Role(person!, role), "A selected account does not have the required role.");
            Require(person!.Id != t.CreatedByUserId && !opposite.Contains(person.Id) && !await Role(person, SystemRoles.Admin) && !await Role(person, SystemRoles.Supplier) && !await Role(person, SystemRoles.SourcingOfficer), "Members must be separate from the other committee, Admin, suppliers and the Sourcing Officer.");
            if (input.CommitteeType == CommitteeType.Top) Require(!await Role(person, SystemRoles.SourcingManager), "The Sourcing Manager cannot sit on the Top Committee.");
        }
        // Delete and insert within the same tender row lock and transaction; no votes/history may exist.
        db.CommitteeMembers.RemoveRange(old);
        await db.SaveChangesAsync();
        for (var i = 0; i < input.Members.Length; i++)
        {
            db.CommitteeMembers.Add(new() { TenderId = t.Id, CommitteeType = input.CommitteeType, Responsibility = seats[i], UserId = input.Members[i], AssignedAt = Now });
            Notify(input.Members[i], t.Id, "Committee assignment", $"You have been assigned to the {input.CommitteeType} Committee for tender #{t.Id}.");
        }
        Audit(actor, t.Id, "CommitteeMembershipChanged", new { input.CommitteeType, Previous = old.Select(x => new { x.UserId, x.Responsibility }), Current = input.Members });
        if (input.CommitteeType == CommitteeType.Top && t.Status == TenderStatus.TopCommitteeReview)
        {
            var session = await Session(t.Id);
            Require(session.State == SessionState.ManagerApproved && RequiresTop(t, session), "This tender is not ready for Top Committee review.");
            if (!await db.CommitteeRounds.AnyAsync(x => x.TenderRoundId == session.TenderRoundId && x.CommitteeType == CommitteeType.Top && x.TenderReportId == session.ReportId))
            {
                var next = (await db.CommitteeRounds.Where(x => x.TenderRoundId == session.TenderRoundId && x.CommitteeType == CommitteeType.Top).MaxAsync(x => (int?)x.RoundNumber) ?? 0) + 1;
                db.CommitteeRounds.Add(new() { TenderRoundId = session.TenderRoundId, CommitteeType = CommitteeType.Top, RoundNumber = next, TenderReportId = session.ReportId, StartedAt = Now });
            }
        }
    });
}
