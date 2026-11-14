using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Round control: re-tender limits, documented approval to proceed with fewer suppliers, supplier
// carry-forward between rounds, external invitations and automatic opening of scheduled scoring.
// Nothing here awards a supplier or changes compliance, weights, scores or approval rules.
public partial class TenderWorkflowService
{
    public Task Proceed(int id, ClaimsPrincipal principal) => Change(id, principal, "ProceedToEvaluationPreparation", async (t, a) =>
    {
        await Officer(t, a); var round = await Round(id);
        Require(round.Status == TenderRoundStatus.Closed && !await db.Set<EvaluationSession>().AnyAsync(x => x.TenderRoundId == round.Id), "Proceed requires the current closed bidding round without an evaluation session.");
        if (await db.Set<RoundDecision>().AnyAsync(x => x.TenderRoundId == round.Id && x.Kind == RoundDecisionKind.Proceed)) return;
        var offers = DistinctSuppliers(await Submitted(round.Id));
        var minimum = MinimumOffers(await FailedRoundsBefore(id, round.RoundNumber));
        Require(offers >= minimum || offers > 0 && await db.Set<RoundDecision>().AnyAsync(x => x.TenderRoundId == round.Id && x.Kind == RoundDecisionKind.ProceedException && x.Status == RoundDecisionStatus.Approved && x.OfferCount == offers), $"At least {minimum} supplier offers are required: re-tender or obtain the existing documented proceed-exception approval.");
        db.Add(new RoundDecision { TenderRoundId = round.Id, Kind = RoundDecisionKind.Proceed, Status = RoundDecisionStatus.Recorded, OfferCount = offers,
            Justification = "Officer selected Proceed for this closed round; compliance, AI, scoring and approvals remain required.", RequestedByUserId = a.Id, RequestedAt = Now, DecidedByUserId = a.Id, DecidedAt = Now });
    });
    private async Task QueueSupplierEmail(TenderRound round, Supplier supplier, ApplicationUser officer)
    {
        if (await db.Set<ExternalSupplierInvitation>().AnyAsync(x => x.TenderRoundId == round.Id && x.Email == supplier.Email)) return;
        db.Add(new ExternalSupplierInvitation { TenderRoundId = round.Id, SupplierId = supplier.Id, CompanyName = supplier.CompanyName, Email = supplier.Email,
            Status = ExternalInvitationStatus.Queued, CreatedByUserId = officer.Id, CreatedAt = Now });
    }
    // Registration itself never grants tender access. Only an approved company matching the
    // originally invited email receives a real current-round invitation.
    public async Task LinkRegisteredContacts(CancellationToken ct)
    {
        var contacts = await db.Set<ExternalSupplierInvitation>().Where(x => x.SupplierId == null && x.TenderRound.Status == TenderRoundStatus.Open && x.TenderRound.Deadline > Now)
            .Select(x => new { x.Id, x.Email, x.TenderRoundId, x.TenderRound.TenderId }).ToListAsync(ct);
        foreach (var contact in contacts)
        {
            var matches = await db.Suppliers.Where(x => x.Status == SupplierStatus.Approved &&
                (x.Email == contact.Email || db.Users.Any(u => u.SupplierId == x.Id && u.IsActive && u.EmailConfirmed && u.Email == contact.Email))).Take(2).ToListAsync(ct);
            if (matches.Count != 1) continue;
            var supplier = matches[0];
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={contact.TenderId}").SingleAsync(ct);
            var round = await db.TenderRounds.SingleAsync(x => x.Id == contact.TenderRoundId, ct);
            if (round.Status == TenderRoundStatus.Open && round.Deadline > Now && !await db.TenderInvitations.AnyAsync(x => x.TenderRoundId == round.Id && x.SupplierId == supplier.Id, ct))
            {
                var officer = await GlobalOfficer();
                db.TenderInvitations.Add(new TenderInvitation { TenderRoundId = round.Id, SupplierId = supplier.Id, InvitedAt = Now, InvitedByUserId = officer.Id });
                foreach (var userId in await db.Users.Where(x => x.SupplierId == supplier.Id && x.IsActive && x.EmailConfirmed).Select(x => x.Id).ToListAsync(ct))
                    Notify(userId, contact.TenderId, "Registration approved: tender invitation", "Your approved supplier account can now accept or decline the tender invitation and review its requirements.");
                Audit(officer, contact.TenderId, "RegisteredExternalSupplierLinked", new { ContactId = contact.Id, SupplierId = supplier.Id, RoundId = round.Id });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }
    // Round 1 plus at most three re-tenders. A fifth round is never created.
    public const int MaxRounds = 4;

    private async Task<int> FailedRoundsBefore(int tenderId, int roundNumber) => await db.TenderRounds
        .CountAsync(x => x.TenderId == tenderId && x.Status == TenderRoundStatus.Failed && x.RoundNumber < roundNumber);
    // Existing rule, unchanged: three offers, or two after three failed rounds.
    private static int MinimumOffers(int failedRounds) => failedRounds >= 3 ? 2 : 3;
    private static int DistinctSuppliers(IEnumerable<Bid> bids) => bids.Select(x => x.TenderInvitation.SupplierId).Distinct().Count();
    private async Task<CommitteeMember?> ManagerSeat(int tenderId) => await db.CommitteeMembers.SingleOrDefaultAsync(x =>
        x.TenderId == tenderId && x.CommitteeType == CommitteeType.Evaluation && x.Responsibility == CommitteeResponsibility.SourcingManager);

    // The officer may re-tender after any closed bidding round, whatever the number of offers, as long as
    // no evaluation has been scheduled for it and a further round is permitted.
    public Task Retender(int id, string reason, ClaimsPrincipal principal) => Change(id, principal, "NewTenderRoundCreated", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 2000, "Record the re-tender reason (up to 2000 characters).");
        // A closed round can be re-tendered before an evaluation is scheduled, or while a scheduled evaluation cannot work because fewer
        // than the required number of compliant offers are eligible. In that case the scheduled evaluation is cancelled and kept as history.
        var session = await db.Set<EvaluationSession>().Include(x => x.EligibleBids).FirstOrDefaultAsync(x => x.TenderRoundId == r.Id);
        Require(r.Status == TenderRoundStatus.Closed, "Only a closed bidding round can be re-tendered.");
        if (session != null)
        {
            Require(session.State is SessionState.Scheduled or SessionState.Incomplete or SessionState.Closed, "The evaluation already produced a report, so this round cannot be re-tendered.");
            Require(session.EligibleBids.Count < MinimumOffers(await FailedRoundsBefore(id, r.RoundNumber)), "Re-tender is allowed after scheduling only when fewer compliant offers than the required minimum are eligible.");
            session.State = SessionState.Closed; session.ClosedAt = Now; session.ClosureReason = "Cancelled: the round was re-tendered because too few compliant offers were eligible.";
        }
        Require(r.RoundNumber < MaxRounds, $"Round {MaxRounds} was the final permitted round. Round {MaxRounds + 1} cannot be created; proceed with the received offers instead.");
        var offers = DistinctSuppliers(await Submitted(r.Id));
        // Re-tendering is the officer's decision: an undecided proceed request for this round is closed, not awaited.
        foreach (var open in await db.Set<RoundDecision>().Where(x => x.TenderRoundId == r.Id && x.Kind == RoundDecisionKind.ProceedException && x.Status == RoundDecisionStatus.Pending).ToListAsync())
        { open.Status = RoundDecisionStatus.Withdrawn; open.DecidedByUserId = a.Id; open.DecidedAt = Now; open.DecisionComment = "Withdrawn: the officer re-tendered this round."; }
        r.Status = TenderRoundStatus.Failed;
        db.TenderRounds.Add(new TenderRound { TenderId = id, RoundNumber = r.RoundNumber + 1, CreatedByUserId = a.Id, CreatedAt = Now });
        db.Add(new RoundDecision { TenderRoundId = r.Id, Kind = RoundDecisionKind.Retender, Status = RoundDecisionStatus.Recorded, OfferCount = offers,
            Justification = reason.Trim(), RequestedByUserId = a.Id, RequestedAt = Now, DecidedByUserId = a.Id, DecidedAt = Now });
        t.Status = TenderStatus.Draft;
        Audit(a, id, "RetenderEvidence", new { FailedRound = r.RoundNumber, NewRound = r.RoundNumber + 1, Offers = offers, Reason = reason.Trim() });
        if (await ManagerSeat(id) is { } manager) Notify(manager.UserId, id, "Tender re-tendered", $"The Sourcing Officer re-tendered round {r.RoundNumber}, which had {offers} offer(s). Round {r.RoundNumber + 1} of {MaxRounds} was created. Earlier invitations and bids remain on record.");
    });

    public Task RequestProceedException(int id, string justification, ClaimsPrincipal principal) => Change(id, principal, "ProceedWithFewerSuppliersRequested", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        Require(!string.IsNullOrWhiteSpace(justification) && justification.Length <= 2000, "A written justification of up to 2000 characters is required.");
        Require(r.Status == TenderRoundStatus.Closed && !await db.Set<EvaluationSession>().AnyAsync(x => x.TenderRoundId == r.Id), "A closed bidding round without an evaluation session is required.");
        var offers = DistinctSuppliers(await Submitted(r.Id));
        var minimum = MinimumOffers(await FailedRoundsBefore(id, r.RoundNumber));
        Require(offers >= 1 && offers < minimum, $"Approval is only needed when at least one but fewer than {minimum} suppliers submitted offers. This round has {offers}.");
        Require(!await db.Set<RoundDecision>().AnyAsync(x => x.TenderRoundId == r.Id && x.Kind == RoundDecisionKind.ProceedException && (x.Status == RoundDecisionStatus.Pending || x.Status == RoundDecisionStatus.Approved)), "This round already has a pending or approved request to proceed.");
        var manager = await ManagerSeat(id);
        Require(manager != null, "The tender has no assigned Sourcing Manager to decide this request.");
        db.Add(new RoundDecision { TenderRoundId = r.Id, Kind = RoundDecisionKind.ProceedException, Status = RoundDecisionStatus.Pending, OfferCount = offers,
            Justification = justification.Trim(), RequestedByUserId = a.Id, RequestedAt = Now });
        Audit(a, id, "ProceedRequestEvidence", new { r.RoundNumber, Offers = offers, NormalMinimum = minimum, Justification = justification.Trim() });
        Notify(manager.UserId, id, "Approval requested: proceed with fewer suppliers", $"Round {r.RoundNumber} received {offers} offer(s); the normal minimum is {minimum}. Review the officer's justification and approve or reject.");
    });

    public Task DecideProceedException(int id, int decisionId, bool approve, string comment, ClaimsPrincipal principal) => Change(id, principal, "ProceedWithFewerSuppliersDecided", async (t, a) =>
    {
        var seat = await ManagerSeat(id);
        Require(seat?.UserId == a.Id && await Role(a, SystemRoles.SourcingManager), "Only the Sourcing Manager assigned to this tender may decide this request.");
        Require(t.CreatedByUserId != a.Id, "The requesting officer cannot approve their own request.");
        Require(!string.IsNullOrWhiteSpace(comment) && comment.Length <= 2000, "A decision comment of up to 2000 characters is required.");
        var r = await Round(id);
        var decision = await db.Set<RoundDecision>().SingleOrDefaultAsync(x => x.Id == decisionId && x.TenderRoundId == r.Id && x.Kind == RoundDecisionKind.ProceedException);
        Require(decision?.Status == RoundDecisionStatus.Pending, "This request is not awaiting a decision for the current round.");
        Require(r.Status == TenderRoundStatus.Closed && !await db.Set<EvaluationSession>().AnyAsync(x => x.TenderRoundId == r.Id), "The round is no longer awaiting a proceed decision.");
        Require(DistinctSuppliers(await Submitted(r.Id)) == decision.OfferCount, "The number of received offers no longer matches the request.");
        decision.Status = approve ? RoundDecisionStatus.Approved : RoundDecisionStatus.Rejected;
        decision.DecidedByUserId = a.Id; decision.DecidedAt = Now; decision.DecisionComment = comment.Trim();
        Audit(a, id, "ProceedDecisionEvidence", new { decision.Id, r.RoundNumber, decision.OfferCount, Approved = approve, Comment = comment.Trim() });
        await NotifyOfficer(id, approve ? "Approved: proceed with fewer suppliers" : "Rejected: proceed with fewer suppliers",
            approve ? "You may schedule the evaluation for this round. Compliance screening, committee evaluation and every approval still apply; nothing is awarded automatically."
                    : r.RoundNumber < MaxRounds ? "The request was rejected. You may re-tender this round." : "The request was rejected. No further round is permitted for this tender.");
    });

    public Task AddExternalSupplier(int id, string companyName, string email, string? phone, ClaimsPrincipal principal) => Change(id, principal, "ExternalSupplierInvited", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        Require(r.Status == TenderRoundStatus.Open && r.Deadline > Now, "Bidding is not open.");
        companyName = (companyName ?? "").Trim(); email = (email ?? "").Trim().ToLowerInvariant(); phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        Require(companyName.Length is > 0 and <= 200 && email.Length <= 256 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && (phone == null || phone.Length <= 40),
            "Company name and a valid email address are required. External invitations need contact information.");
        Require(!await db.Suppliers.AnyAsync(x => x.Email == email) && !await db.Users.AnyAsync(x => x.SupplierId != null && x.NormalizedEmail == email.ToUpperInvariant()),
            "This email belongs to a registered supplier. Invite the supplier through its registered account instead.");
        Require(!await db.Set<ExternalSupplierInvitation>().AnyAsync(x => x.TenderRoundId == r.Id && x.Email == email), "This contact was already invited in the current round.");
        db.Add(new ExternalSupplierInvitation { TenderRoundId = r.Id, CompanyName = companyName, Email = email, Phone = phone, Status = ExternalInvitationStatus.Queued, CreatedByUserId = a.Id, CreatedAt = Now });
        Audit(a, id, "ExternalInvitationEvidence", new { r.RoundNumber, companyName, email, phone });
    });

    // Called when bidding opens for round 2 and later. Registered suppliers that are still approved get a
    // fresh invitation and a notification; unregistered contacts are queued for the external channel.
    // Earlier invitations and bids are untouched, and no bid is copied: each round needs a new submission.
    private async Task CarryForwardSuppliers(Tender t, ApplicationUser officer, TenderRound round)
    {
        var supplierIds = await db.TenderInvitations.Where(x => x.TenderRound.TenderId == t.Id && x.TenderRoundId != round.Id && x.Supplier.Status == SupplierStatus.Approved)
            .Select(x => x.SupplierId).Distinct().ToListAsync();
        var already = await db.TenderInvitations.Where(x => x.TenderRoundId == round.Id).Select(x => x.SupplierId).ToListAsync();
        var invited = supplierIds.Except(already).ToList();
        foreach (var supplierId in invited)
        {
            db.TenderInvitations.Add(new TenderInvitation { TenderRoundId = round.Id, SupplierId = supplierId, InvitedAt = Now, InvitedByUserId = officer.Id });
            foreach (var userId in await db.Users.Where(x => x.SupplierId == supplierId && x.IsActive).Select(x => x.Id).ToListAsync())
                Notify(userId, t.Id, $"New tender round {round.RoundNumber}", "This tender was re-tendered. Your registration, documents and earlier bids remain on record, but a new bid must be submitted for this round. Accept or decline the new invitation.");
            await QueueSupplierEmail(round, await db.Suppliers.SingleAsync(x => x.Id == supplierId), officer);
        }
        var contacts = await db.Set<ExternalSupplierInvitation>().Where(x => x.TenderRound.TenderId == t.Id && x.TenderRoundId != round.Id && x.SupplierId == null)
            .OrderByDescending(x => x.Id).Select(x => new { x.CompanyName, x.Email, x.Phone }).ToListAsync();
        var queued = 0;
        foreach (var contact in contacts.GroupBy(x => x.Email).Select(x => x.First()))
        {
            if (await db.Suppliers.AnyAsync(x => x.Email == contact.Email) || await db.Set<ExternalSupplierInvitation>().AnyAsync(x => x.TenderRoundId == round.Id && x.Email == contact.Email)) continue;
            db.Add(new ExternalSupplierInvitation { TenderRoundId = round.Id, CompanyName = contact.CompanyName, Email = contact.Email, Phone = contact.Phone,
                Status = ExternalInvitationStatus.Queued, CreatedByUserId = officer.Id, CreatedAt = Now });
            queued++;
        }
        Audit(officer, t.Id, "RoundSuppliersNotified", new { round.RoundNumber, RegisteredSupplierIds = invited, ExternalContactsQueued = queued });
    }

    // Scoring opens by time alone (SubmitEvaluation checks the window). This records the opening once and
    // tells the four assigned members and the officer.
    public async Task OpenDueSessions(CancellationToken ct)
    {
        var ids = await db.Set<EvaluationSession>().Where(x => x.State == SessionState.Scheduled && x.OpenedAt == null && x.StartsAt <= Now && x.EndsAt > Now)
            .Select(x => x.TenderRound.TenderId).Distinct().ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var tender = await db.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleAsync(ct);
            var s = await Session(id);
            if (s.State == SessionState.Scheduled && s.OpenedAt == null && Now >= s.StartsAt && Now < s.EndsAt)
            {
                s.OpenedAt = Now;
                foreach (var member in await Members(id, CommitteeType.Evaluation)) NotifyMeeting(member.UserId, id, "Evaluation scoring is open", $"Scoring opened as scheduled and closes automatically at {s.EndsAt:u}. Only the four assigned members may submit scores.");
                await NotifyOfficer(id, "Evaluation scoring opened", $"Scoring opened as scheduled and closes automatically at {s.EndsAt:u}.");
                db.AuditLogs.Add(new AuditLog { EntityName = "Tender", EntityId = id.ToString(), Action = "EvaluationAutoOpened", CreatedAt = Now });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }

    private async Task LoadRoundControl(WorkflowDetails result, Tender t, ApplicationUser a, bool officer, bool supplier)
    {
        var current = t.Rounds.OrderByDescending(x => x.RoundNumber).FirstOrDefault();
        result.MaxRounds = MaxRounds;
        if (current == null) return;
        if (supplier)
        {
            result.EarlierBids = await db.Bids.AsNoTracking().Include(x => x.TenderInvitation).ThenInclude(x => x.TenderRound)
                .Where(x => x.TenderInvitation.TenderRound.TenderId == t.Id && x.TenderInvitation.TenderRoundId != current.Id && x.TenderInvitation.SupplierId == a.SupplierId).ToListAsync();
            return;
        }
        var managerSeat = t.CommitteeMembers.Any(x => x.UserId == a.Id && x.Responsibility == CommitteeResponsibility.SourcingManager) && await Role(a, SystemRoles.SourcingManager);
        if (!officer && !managerSeat) return;
        var roundIds = t.Rounds.Select(x => x.Id).ToList();
        result.RoundDecisions = await db.Set<RoundDecision>().AsNoTracking().Where(x => roundIds.Contains(x.TenderRoundId)).OrderBy(x => x.Id).ToListAsync();
        result.MinimumOffers = MinimumOffers(await FailedRoundsBefore(t.Id, current.RoundNumber));
        result.DistinctOffers = await db.Bids.Where(x => x.TenderInvitation.TenderRoundId == current.Id && x.Status == BidStatus.Submitted).Select(x => x.TenderInvitation.SupplierId).Distinct().CountAsync();
        if (!officer) return;
        result.ExternalInvitations = await db.Set<ExternalSupplierInvitation>().AsNoTracking().Where(x => roundIds.Contains(x.TenderRoundId)).OrderBy(x => x.Id).ToListAsync();
        result.EarlierBids = await db.Bids.AsNoTracking().Include(x => x.TenderInvitation).ThenInclude(x => x.TenderRound)
            .Where(x => x.TenderInvitation.TenderRound.TenderId == t.Id && x.TenderInvitation.TenderRoundId != current.Id).ToListAsync();
        var submitted = await db.Bids.Where(x => roundIds.Contains(x.TenderInvitation.TenderRoundId) && x.Status == BidStatus.Submitted)
            .GroupBy(x => x.TenderInvitation.TenderRoundId).Select(x => new { RoundId = x.Key, Count = x.Count() }).ToListAsync();
        result.RoundHistory = t.Rounds.OrderBy(x => x.RoundNumber).Select(x => new RoundSummary(x.Id, x.RoundNumber, x.Status.ToString(), x.OpensAt, x.Deadline, x.Invitations.Count,
            submitted.SingleOrDefault(y => y.RoundId == x.Id)?.Count ?? 0, result.ExternalInvitations.Count(y => y.TenderRoundId == x.Id))).ToList();
    }
}
