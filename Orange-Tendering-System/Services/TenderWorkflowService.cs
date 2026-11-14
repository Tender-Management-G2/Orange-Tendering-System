using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;
using Orange_Tendering_System.Services.Reports;

namespace Orange_Tendering_System.Services;

public partial class TenderWorkflowService(ApplicationDbContext db, UserManager<ApplicationUser> users,
    IOptions<TenderApprovalPolicy> policyOptions, TimeProvider clock, ReportDocumentService reportDocuments,
    IOptions<ReportRevisionPolicy> revisionPolicyOptions, IOptions<ReportSigningOptions> reportSigningOptions)
{
    private TenderApprovalPolicy Policy => policyOptions.Value;
    public DateTime Now => clock.GetUtcNow().UtcDateTime;
    public static void Require([DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new WorkflowException(message); }
    public async Task<ApplicationUser> Actor(ClaimsPrincipal principal)
    {
        var actor = await users.GetUserAsync(principal);
        Require(actor is { IsActive: true }, "An active authenticated account is required.");
        return actor!;
    }
    public Task<bool> Role(ApplicationUser actor, string role) => users.IsInRoleAsync(actor, role);
    public async Task<bool> IsOfficer(ApplicationUser actor)
    {
        if (!await Role(actor, SystemRoles.SourcingOfficer)) return false;
        var officers = (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Where(x => x.IsActive).ToArray();
        return officers.Length == 1 && officers[0].Id == actor.Id;
    }
    private async Task<ApplicationUser> GlobalOfficer()
    {
        var officers = (await users.GetUsersInRoleAsync(SystemRoles.SourcingOfficer)).Where(x => x.IsActive).ToArray();
        Require(officers.Length == 1, "Configure exactly one active Sourcing Officer for all tenders.");
        return officers[0];
    }
    private async Task Officer(Tender tender, ApplicationUser actor) => Require(await IsOfficer(actor), "Only the single active Sourcing Officer may manage tenders.");
    public async Task<bool> WeightApprover(ApplicationUser actor) => Policy.WeightConfigured &&
        (Policy.WeightApproverUserIds.Contains(actor.Id) || (await users.GetRolesAsync(actor)).Intersect(Policy.WeightApproverRoles).Any());
    private async Task<List<CommitteeMember>> Members(int tenderId, CommitteeType type) => await db.CommitteeMembers
        .Where(x => x.TenderId == tenderId && x.CommitteeType == type).ToListAsync();
    private async Task<CommitteeMember> Member(Tender tender, ApplicationUser actor, CommitteeType type)
    {
        var member = await db.CommitteeMembers.SingleOrDefaultAsync(x => x.TenderId == tender.Id && x.UserId == actor.Id && x.CommitteeType == type);
        Require(member != null, "You are not assigned to this committee.");
        Require(type == CommitteeType.Top ? await Role(actor, SystemRoles.TopCommittee) :
            await Role(actor, SystemRoles.EvaluationCommittee) || (member!.Responsibility == CommitteeResponsibility.SourcingManager && await Role(actor, SystemRoles.SourcingManager)), "The committee role is required.");
        if (type == CommitteeType.Top) Require(!await Role(actor, SystemRoles.SourcingOfficer) && !await Role(actor, SystemRoles.SourcingManager), "Officer and Manager cannot vote on the Top Committee.");
        return member!;
    }
    private void Audit(ApplicationUser actor, int tenderId, string action, object? evidence = null) => db.AuditLogs.Add(new AuditLog
    { UserId = actor.Id, EntityName = "Tender", EntityId = tenderId.ToString(), Action = action, NewValues = JsonSerializer.Serialize(evidence), CreatedAt = Now });
    private void Notify(string userId, int tenderId, string title, string message) => db.Notifications.Add(new Notification
    { UserId = userId, Title = title, Message = message, Link = $"/Workflow/Details/{tenderId}", CreatedAt = Now });
    // Notification whose link opens the evaluation meeting page ("Join meeting") for the person it is sent to.
    private void NotifyMeeting(string userId, int tenderId, string title, string message) => db.Notifications.Add(new Notification
    { UserId = userId, Title = title, Message = message, Link = $"/Meetings/Join/{tenderId}", CreatedAt = Now });
    private async Task NotifyOfficerMeeting(int tenderId, string message) => NotifyMeeting((await GlobalOfficer()).Id, tenderId, "Evaluation meeting scheduled", message);
    private async Task NotifyOfficer(int tenderId, string title, string message) => Notify((await GlobalOfficer()).Id, tenderId, title, message);

    // Every mutation of a tender takes the same SQL Server row lock, including bidding,
    // closure, submissions, decisions and award. Unique constraints are a second defense.
    private async Task Change(int tenderId, ClaimsPrincipal principal, string action,
        Func<Tender, ApplicationUser, Task> operation, Func<DateTime?>? deadline = null)
    {
        var actor = await Actor(principal);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var tender = await db.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={tenderId}").SingleOrDefaultAsync();
        Require(tender != null, "Tender not found.");
        await operation(tender!, actor);
        Audit(actor, tenderId, action);
        await db.SaveChangesAsync();
        if (deadline != null) Require(deadline() is { } end && Now < end, "The submission deadline has passed. Nothing was submitted.");
        await tx.CommitAsync();
    }
    private async Task<WeightRound> Weights(int tenderId) => await db.Set<WeightRound>().Include(x => x.Proposals)
        .OrderByDescending(x => x.Version).FirstOrDefaultAsync(x => x.TenderId == tenderId) ?? throw new WorkflowException("Send the weight request first.");
    private async Task<TenderRound> Round(int tenderId) => await db.TenderRounds.OrderByDescending(x => x.RoundNumber)
        .FirstAsync(x => x.TenderId == tenderId);
    private async Task<EvaluationSession> Session(int tenderId) => await db.Set<EvaluationSession>()
        .Include(x => x.WeightRound).Include(x => x.CommitteeRound).ThenInclude(x => x.Criteria)
        .Include(x => x.EligibleBids).Include(x => x.Invitations)
        .Include(x => x.Report).Include(x => x.TenderRound)
        .SingleOrDefaultAsync(x => x.TenderRound.TenderId == tenderId && x.TenderRound.RoundNumber == db.TenderRounds.Where(r => r.TenderId == tenderId).Max(r => r.RoundNumber))
        ?? throw new WorkflowException("No evaluation session is configured.");
    private async Task<List<Bid>> Submitted(int roundId) => await db.Bids.Include(x => x.Documents).Include(x => x.Items)
        .Include(x => x.TenderInvitation).ThenInclude(x => x.Supplier)
        .Where(x => x.TenderInvitation.TenderRoundId == roundId && x.Status == BidStatus.Submitted).ToListAsync();
    private void Closed(EvaluationSession session, string? actorId = null, string? reason = null)
    {
        if (session.State != SessionState.Scheduled) return;
        session.ClosedAt = Now; session.ClosedByUserId = actorId; session.ClosureReason = reason ?? "Scheduled deadline reached";
        session.State = session.Submissions.Count == 4 ? SessionState.Closed : SessionState.Incomplete;
    }

    public async Task<int> Create(RequirementsInput input, ClaimsPrincipal principal)
    {
        var actor = await Actor(principal);
        Require(await IsOfficer(actor), "Exactly one active Sourcing Officer must be configured.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var pr = await db.PurchaseRequests.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == input.PurchaseRequestId);
        Require(pr?.Status == PurchaseRequestStatus.Accepted && pr.Items.Count > 0, "An accepted Purchase Request with line items is required.");
        var ids = new[] { input.SourcingManagerId, input.DepartmentRepresentativeId, input.FinancialRepresentativeId, input.TechnicalRepresentativeId };
        Require(ids.Distinct().Count() == 4 && !ids.Contains(actor.Id), "Assign four distinct committee members, separate from the officer.");
        for (var i = 0; i < ids.Length; i++)
        {
            var person = await users.FindByIdAsync(ids[i]);
            Require(person is { IsActive: true }, "Committee account missing or inactive.");
            Require(await Role(person!, i == 0 ? SystemRoles.SourcingManager : SystemRoles.EvaluationCommittee), "Each member must have the appropriate Identity role.");
            if (i == 1) Require(person!.DepartmentId == pr!.DepartmentId, "Department representative must belong to the requesting department.");
        }
        var requirements = JsonSerializer.Serialize(new { input.TechnicalRequirements, input.CommercialConditions, input.DeliveryExpectations, input.MandatoryCompliance });
        var tender = new Tender { PurchaseRequestId = pr!.Id, Title = input.Title, Description = input.Description, Requirements = requirements,
            CreatedByUserId = actor.Id, CreatedAt = Now, EstimatedValue = input.EstimatedValue, Currency = input.Currency, IsCritical = input.IsCritical,
            Items = pr.Items.Select(x => new TenderItem { Name = x.Name, Quantity = x.Quantity, Specifications = x.Specifications, Unit = x.Unit }).ToList() };
        db.Tenders.Add(tender);
        await db.SaveChangesAsync();
        db.TenderRounds.Add(new TenderRound { TenderId = tender.Id, RoundNumber = 1, CreatedByUserId = actor.Id, CreatedAt = Now });
        for (var i = 0; i < ids.Length; i++) db.CommitteeMembers.Add(new CommitteeMember { TenderId = tender.Id, UserId = ids[i], CommitteeType = CommitteeType.Evaluation, Responsibility = (CommitteeResponsibility)i, AssignedAt = Now });
        Audit(actor, tender.Id, "RequirementsCreated", input);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return tender.Id;
    }
    public Task RequestWeights(int id, ClaimsPrincipal principal) => Change(id, principal, "WeightRequestSent", async (t, a) =>
    {
        await Officer(t, a);
        Require(t.Status == TenderStatus.Draft && !await db.Set<WeightRound>().AnyAsync(x => x.TenderId == id), "Requirements are frozen after the first request. Exceptional revision policy is required.");
        var members = await Members(id, CommitteeType.Evaluation);
        Require(members.Count == 4 && members.Select(x => x.Responsibility).Distinct().Count() == 4 && members.All(x => x.Responsibility.HasValue), "All four distinct committee seats are required.");
        var items = await db.TenderItems.Where(x => x.TenderId == id).ToListAsync();
        var documents = await db.Documents.Where(x => x.TenderId == id).Select(x => new { x.Id, x.FileName }).ToListAsync();
        db.Add(new WeightRound { TenderId = id, Version = 1, CreatedAt = Now,
            RequirementsJson = JsonSerializer.Serialize(new { t.Title, t.Description, t.PurchaseRequestId, t.Requirements, Items = items.Select(x => new { x.Name, x.Quantity, x.Unit, x.Specifications }), Documents = documents }) });
        foreach (var m in members) Notify(m.UserId, id, "Evaluation Weight Request", "Review requirements version 1 and submit your independent weight proposal.");
    });
    public Task Propose(int id, decimal financial, decimal technical, ClaimsPrincipal principal) => Change(id, principal, "WeightProposalSubmitted", async (t, a) =>
    {
        WeightedCalculation.Weights(financial, technical);
        var member = await Member(t, a, CommitteeType.Evaluation); var weights = await Weights(id);
        Require(weights.State == WeightState.Proposals && weights.Proposals.All(x => x.MemberUserId != a.Id), "This round is closed or you have already submitted.");
        weights.Proposals.Add(new WeightProposal { CommitteeMemberId = member.Id, MemberUserId = a.Id, Financial = financial, Technical = technical, SubmittedAt = Now });
        if (weights.Proposals.Count == 4)
        {
            var avg = WeightedCalculation.Average(weights.Proposals.Select(x => (x.Financial, x.Technical)));
            weights.FinancialAverage = avg.Financial; weights.TechnicalAverage = avg.Technical; weights.State = WeightState.AwaitingApproval;
            await NotifyOfficer(id, "Weight proposals complete", Policy.WeightConfigured ? "Awaiting formal weight approval." : "Awaiting Approval Policy: bidding remains blocked.");
        }
        Audit(a, id, "ProposalEvidence", new { weights.Version, member.Id, financial, technical });
    });
    public Task ApproveWeights(int id, ClaimsPrincipal principal) => Change(id, principal, "WeightsApproved", async (t, a) =>
    {
        Require(await WeightApprover(a), "Awaiting Approval Policy, or you are not an authorized weight approver.");
        Require(await Role(a, SystemRoles.SourcingManager) && (await ManagerSeat(id))?.UserId == a.Id, "Only the Sourcing Manager assigned to this tender may approve its calculated weights.");
        var w = await Weights(id);
        Require(t.Status == TenderStatus.Draft && w.State == WeightState.AwaitingApproval && w.Proposals.Count == 4, "Weights are not awaiting approval.");
        var avg = WeightedCalculation.Average(w.Proposals.Select(x => (x.Financial, x.Technical)));
        Require(avg.Financial == w.FinancialAverage && avg.Technical == w.TechnicalAverage, "Calculated values do not match the proposals.");
        w.ApprovedFinancial = avg.Financial; w.ApprovedTechnical = avg.Technical; w.ApprovedAt = Now; w.ApprovedByUserId = a.Id;
        w.ApprovalPolicyJson = JsonSerializer.Serialize(Policy); w.State = WeightState.Approved;
        await NotifyOfficer(id, "Weights approved and locked", "The approved evaluation weights may now be published with the tender.");
    });
    public Task OpenBidding(int id, DateTimeOffset deadline, ClaimsPrincipal principal) => Change(id, principal, "BiddingOpened", async (t, a) =>
    {
        await Officer(t, a); var w = await Weights(id); var round = await Round(id);
        Require(t.Status == TenderStatus.Draft && round.Status == TenderRoundStatus.Draft && w.State == WeightState.Approved, "Approved locked weights are required before bidding opens.");
        Require(deadline.UtcDateTime > Now, "Bidding deadline must be in the future.");
        round.OpensAt = Now; round.Deadline = deadline.UtcDateTime; round.Status = TenderRoundStatus.Open; t.Status = TenderStatus.Bidding;
        if (round.RoundNumber > 1) await CarryForwardSuppliers(t, a, round);
    });
    public Task Invite(int id, int supplierId, ClaimsPrincipal principal) => Change(id, principal, "SupplierInvited", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        Require(r.Status == TenderRoundStatus.Open && r.Deadline > Now, "Bidding is not open.");
        Require(await db.Suppliers.AnyAsync(x => x.Id == supplierId && x.Status == SupplierStatus.Approved), "Only approved suppliers can be invited.");
        Require(!await db.TenderInvitations.AnyAsync(x => x.TenderRoundId == r.Id && x.SupplierId == supplierId), "Supplier already invited.");
        db.TenderInvitations.Add(new TenderInvitation { TenderRoundId = r.Id, SupplierId = supplierId, InvitedAt = Now, InvitedByUserId = a.Id });
        await QueueSupplierEmail(r, await db.Suppliers.SingleAsync(x => x.Id == supplierId), a);
        foreach (var u in await db.Users.Where(x => x.SupplierId == supplierId && x.IsActive).ToListAsync()) Notify(u.Id, id, "Tender invitation", "Review the published requirements and approved weights, then accept or decline.");
    });
    public Task Respond(int id, int invitationId, bool accept, ClaimsPrincipal principal) => Change(id, principal, "InvitationResponded", async (t, a) =>
    {
        var r = await Round(id);
        var invitation = await db.TenderInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.TenderRoundId == r.Id && x.SupplierId == a.SupplierId);
        Require(await Role(a, SystemRoles.Supplier) && invitation?.Status == InvitationStatus.Pending && r.Status == TenderRoundStatus.Open && r.Deadline > Now, "Invitation is unavailable or no longer open.");
        invitation!.Status = accept ? InvitationStatus.Accepted : InvitationStatus.Declined; invitation.RespondedAt = Now;
    });
    public Task SubmitBid(int id, BidInput input, ClaimsPrincipal principal)
    {
        DateTime? limit = null;
        return Change(id, principal, "BidSubmitted", async (t, a) =>
        {
            var r = await Round(id); limit = r.Deadline; var w = await Weights(id);
            var invite = await db.TenderInvitations.Include(x => x.Supplier).SingleOrDefaultAsync(x => x.Id == input.InvitationId && x.TenderRoundId == r.Id && x.SupplierId == a.SupplierId);
            Require(await Role(a, SystemRoles.Supplier) && invite?.Status == InvitationStatus.Accepted && invite.Supplier.Status == SupplierStatus.Approved, "An accepted invitation and eligible supplier account are required.");
            Require(t.Status == TenderStatus.Bidding && r.Status == TenderRoundStatus.Open && r.Deadline > Now && w.State == WeightState.Approved, "Bidding is closed or weights are not approved.");
            Require(!await db.Bids.AnyAsync(x => x.TenderInvitationId == input.InvitationId), "This invitation already has a bid. Submitted bids are immutable; revision policy is required.");
            var items = await db.TenderItems.Where(x => x.TenderId == id).ToListAsync();
            Require(input.Lines.Count == items.Count && input.Lines.Select(x => x.TenderItemId).Distinct().Count() == items.Count && input.Lines.All(x => items.Any(y => y.Id == x.TenderItemId) && x.UnitPrice.HasValue && x.UnitPrice >= 0 && decimal.Round(x.UnitPrice.Value, 2) == x.UnitPrice), "Price every tender item exactly once using two decimal places.");
            var bid = new Bid { TenderInvitationId = input.InvitationId, Status = BidStatus.Submitted, TechnicalProposal = input.TechnicalProposal, CurrencyCode = input.Currency, DeliveryDays = input.DeliveryDays, PaymentTerms = input.PaymentTerms, OtherCost = input.ExtraCosts, CreatedAt = Now, SubmittedAt = Now,
                Items = input.Lines.Select(x => new BidItem { TenderItemId = x.TenderItemId, UnitPrice = x.UnitPrice!.Value, Quantity = items.Single(y => y.Id == x.TenderItemId).Quantity, OfferedSpecifications = x.Specifications }).ToList() };
            db.Bids.Add(bid);
            await NotifyOfficer(id, "Bid received", "An invited supplier submitted an offer. Compliance review is required after bidding closes.");
        }, () => limit);
    }
    // The supplier portal's rich draft enters the same locked workflow boundary as direct submissions.
    public async Task SubmitDraft(int bidId, ClaimsPrincipal principal)
    {
        var tenderId = await db.Bids.Where(b => b.Id == bidId).Select(b => (int?)b.TenderInvitation.TenderRound.TenderId).SingleOrDefaultAsync();
        Require(tenderId.HasValue, "Draft offer not found.");
        DateTime? deadline = null;
        await Change(tenderId.Value, principal, "DraftOfferSubmitted", async (t, actor) =>
        {
            var round = await Round(t.Id); deadline = round.Deadline;
            var weights = await Weights(t.Id);
            var bid = await db.Bids.Include(b => b.Items).Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier).SingleAsync(b => b.Id == bidId);
            Require(await Role(actor, SystemRoles.Supplier) && actor.SupplierId == bid.TenderInvitation.SupplierId && bid.TenderInvitation.Supplier.Status == SupplierStatus.Approved,
                "The owning approved supplier account is required.");
            Require(bid.Status == BidStatus.Draft && bid.TenderInvitation.Status == InvitationStatus.Accepted && bid.TenderInvitation.TenderRoundId == round.Id &&
                t.Status == TenderStatus.Bidding && round.Status == TenderRoundStatus.Open && round.Deadline > Now && (!round.OpensAt.HasValue || round.OpensAt <= Now) && weights.State == WeightState.Approved,
                "The draft is unavailable, bidding is closed, or the weights are not approved.");
            Require(!string.IsNullOrWhiteSpace(bid.TechnicalProposal) && bid.DeliveryDays is > 0 && bid.Items.Count > 0 && bid.Items.All(i => i.Quantity > 0 && i.UnitPrice > 0),
                "Complete the technical proposal, delivery days and positive quantities and prices.");
            Require(!bid.ValidUntil.HasValue || bid.ValidUntil.Value.Date >= Now.Date, "Offer validity has expired.");
            Require(bid.ShippingCost >= 0 && bid.InstallationCost >= 0 && bid.MaintenanceCost >= 0 && bid.TaxAmount >= 0 && bid.OtherCost >= 0, "Offer costs cannot be negative.");
            var expected = await db.TenderItems.Where(i => i.TenderId == t.Id).Select(i => i.Id).ToListAsync();
            Require(expected.Order().SequenceEqual(bid.Items.Select(i => i.TenderItemId).Order()), "Price every tender item exactly once.");
            Require(!await db.Bids.AnyAsync(b => b.TenderInvitationId == bid.TenderInvitationId && b.Id != bidId && b.Status == BidStatus.Submitted), "A final offer already exists.");
            bid.Status = BidStatus.Submitted; bid.SubmittedAt = Now;
            db.AuditLogs.Add(new AuditLog { UserId = actor.Id, EntityName = nameof(Bid), EntityId = bid.Id.ToString(), Action = "SubmitOffer", CreatedAt = Now });
            await NotifyOfficer(t.Id, "Supplier offer submitted", $"Offer #{bid.Id} is ready for compliance review after bidding closes.");
        }, () => deadline);
    }

    public Task CloseBidding(int id, ClaimsPrincipal principal) => Change(id, principal, "BiddingClosed", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        // Bidding closes after its deadline, or earlier once every invited supplier has either declined or submitted its offer (nothing is left to wait for).
        var invitations = await db.TenderInvitations.Where(i => i.TenderRoundId == r.Id).Select(i => new { i.Id, i.Status }).ToListAsync();
        var submitted = (await db.Bids.Where(b => b.Status == BidStatus.Submitted && b.TenderInvitation.TenderRoundId == r.Id).Select(b => b.TenderInvitationId).ToListAsync()).ToHashSet();
        var everyoneAnswered = invitations.Count > 0 && invitations.All(i => i.Status == InvitationStatus.Declined || submitted.Contains(i.Id));
        Require(r.Status == TenderRoundStatus.Open && (r.Deadline <= Now || everyoneAnswered), "Bidding can close only after its deadline, or once every invited supplier has submitted an offer or declined.");
        r.Status = TenderRoundStatus.Closed; r.ClosedAt = Now;
    });
    public Task Compliance(int id, int bidId, ComplianceStatus status, string notes, string missing, ClaimsPrincipal principal) => Change(id, principal, "ComplianceRecorded", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        Require(r.Status == TenderRoundStatus.Closed && !await db.Set<EvaluationSession>().AnyAsync(x => x.TenderRoundId == r.Id), "Compliance may only change after bidding closes and before evaluation setup.");
        var bid = (await Submitted(r.Id)).SingleOrDefault(x => x.Id == bidId);
        Require(bid != null && status is ComplianceStatus.Compliant or ComplianceStatus.NonCompliant or ComplianceStatus.PendingClarification && !string.IsNullOrWhiteSpace(notes), "Select a valid compliance result and record your reasons.");
        if (status == ComplianceStatus.Compliant) Require(bid!.Documents.Count > 0 && string.IsNullOrWhiteSpace(missing), "Missing documents or requirements cannot be marked compliant.");
        bid!.ComplianceStatus = status; bid.ComplianceNotes = notes; bid.MissingRequirements = missing; bid.ComplianceCheckedByUserId = a.Id; bid.ComplianceCheckedAt = Now;
        Audit(a, id, "ComplianceEvidence", new { bidId, status, notes, missing });
    });
    // Every schedule precondition except the meeting link. Shared by Setup and by the read-only pre-check that
    // runs before an external Zoom meeting is created.
    private async Task<(TenderRound Round, WeightRound Weights, List<Bid> Bids, AiAnalysis Ai, List<CommitteeMember> Members)> SetupChecks(Tender t, ApplicationUser a, SessionInput input)
    {
        var id = t.Id;
        await Officer(t, a); var r = await Round(id); var w = await Weights(id); var bids = await Submitted(r.Id);
        Require(r.Status == TenderRoundStatus.Closed && w.State == WeightState.Approved && !await db.Set<EvaluationSession>().AnyAsync(x => x.TenderRoundId == r.Id), "A closed bidding round with locked approved weights and no existing session is required.");
        Require(await db.Set<RoundDecision>().AnyAsync(x => x.TenderRoundId == r.Id && x.Kind == RoundDecisionKind.Proceed), "The Officer must explicitly select Proceed for this closed round before scheduling evaluation.");
        var failedRounds = await FailedRoundsBefore(id, r.RoundNumber);
        var minimumOffers = MinimumOffers(failedRounds);
        var offers = DistinctSuppliers(bids);
        // A documented, manager-approved exception relaxes only the offer count for this round.
        var exception = await db.Set<RoundDecision>().AnyAsync(x => x.TenderRoundId == r.Id && x.Kind == RoundDecisionKind.ProceedException && x.Status == RoundDecisionStatus.Approved && x.OfferCount == offers);
        Require(offers >= minimumOffers || exception && offers >= 1, $"At least {minimumOffers} submitted offers from distinct suppliers are required for this round. The two-offer exception requires three prior failed rounds. Otherwise re-tender, or obtain documented Sourcing Manager approval to proceed with fewer suppliers.");
        Require(bids.All(x => x.ComplianceStatus is ComplianceStatus.Compliant or ComplianceStatus.NonCompliant) && bids.Any(x => x.ComplianceStatus == ComplianceStatus.Compliant && x.TenderInvitation.Supplier.Status == SupplierStatus.Approved), "Complete compliance screening for every offer, resolve pending clarifications, and retain at least one eligible approved supplier.");
        Require(input.StartsAt.UtcDateTime >= Now && input.EndsAt > input.StartsAt, "Start must be in the future and EndTime must be after StartTime.");
        var ai = await db.Set<AiAnalysis>().SingleOrDefaultAsync(x => x.Id == input.AiAnalysisId && x.TenderRoundId == r.Id);
        Require(ai?.Status == "Completed" && ai.ReviewedAt != null, "Attach an officer-reviewed AI analysis report. AI is supplementary and unverified until reviewed.");
        var members = await Members(id, CommitteeType.Evaluation); Require(members.Count == 4, "Four committee members are required.");
        return (r, w, bids, ai!, members);
    }
    public async Task ValidateSetup(int id, SessionInput input, ClaimsPrincipal principal)
    {
        var a = await Actor(principal); var t = await db.Tenders.SingleOrDefaultAsync(x => x.Id == id);
        Require(t != null, "Tender not found.");
        await SetupChecks(t, a, input);
    }
    public Task Setup(int id, SessionInput input, ClaimsPrincipal principal, string? zoomMeetingId = null) => Change(id, principal, "EvaluationScheduled", async (t, a) =>
    {
        var (r, w, bids, ai, members) = await SetupChecks(t, a, input);
        Require(Uri.TryCreate(input.MeetingUrl, UriKind.Absolute, out var url) && url.Scheme == "https" && (url.Host == "zoom.us" || url.Host.EndsWith(".zoom.us", StringComparison.OrdinalIgnoreCase)
            || (url.Host == "meet.jit.si" && url.AbsolutePath.StartsWith("/OrangeTender", StringComparison.Ordinal))) && input.MeetingUrl!.Length <= 1000, "Provide an HTTPS Zoom meeting link (or create the meeting automatically).");
        Require(zoomMeetingId == null || zoomMeetingId.Length <= 40, "Invalid Zoom meeting identifier.");
        var cr = new CommitteeRound { TenderRoundId = r.Id, CommitteeType = CommitteeType.Evaluation, RoundNumber = 1, StartedAt = Now, FinancialWeight = w.ApprovedFinancial, TechnicalWeight = w.ApprovedTechnical };
        cr.Criteria.Add(new EvaluationCriterion { Name = "Financial", Type = CriterionType.Financial, Weight = w.ApprovedFinancial, MaxScore = 100 });
        cr.Criteria.Add(new EvaluationCriterion { Name = "Technical", Type = CriterionType.Technical, Weight = w.ApprovedTechnical, MaxScore = 100 });
        var eligible = bids.Where(x => x.ComplianceStatus == ComplianceStatus.Compliant && x.TenderInvitation.Supplier.Status == SupplierStatus.Approved).ToList();
        var session = new EvaluationSession { TenderRoundId = r.Id, WeightRoundId = w.Id, CommitteeRound = cr, AiAnalysisId = ai.Id, StartsAt = input.StartsAt.UtcDateTime, EndsAt = input.EndsAt.UtcDateTime, MeetingUrl = input.MeetingUrl!, ZoomMeetingId = zoomMeetingId,
            EligibleBids = eligible.Select(x => new SessionBid { BidId = x.Id }).ToList(),
            Invitations = members.Select(x => new EvaluationParticipation { CommitteeMemberId = x.Id, SentAt = Now }).ToList() };
        db.Add(session); t.Status = TenderStatus.Evaluation; r.Status = TenderRoundStatus.SentToEvaluation;
        var attachedDocuments = eligible.Sum(x => x.Documents.Count);
        var meeting = zoomMeetingId != null ? $"Zoom meeting {zoomMeetingId} was created for this session. Use Join meeting in this notification."
            : input.MeetingUrl!.Contains("meet.jit.si") ? "The system created the video meeting room for this session. Use Join meeting in this notification at the scheduled time." : "The Zoom link was supplied by the officer; open it from the tender page.";
        await NotifyOfficerMeeting(id, $"Evaluation meeting scheduled for {session.StartsAt:u}. {meeting}");
        foreach (var m in members) NotifyMeeting(m.UserId, id, "Evaluation invitation", $"Scoring opens automatically at {session.StartsAt:u} and closes automatically at {session.EndsAt:u}. {meeting} The reviewed AI analysis #{ai.Id} and {attachedDocuments} supplier document(s) for {eligible.Count} eligible offer(s) are attached on the tender page.");
        Audit(a, id, "EvaluationScheduleEvidence", new { r.RoundNumber, session.StartsAt, session.EndsAt, ZoomMeetingId = zoomMeetingId, AiAnalysisId = ai.Id, EligibleBidIds = eligible.Select(x => x.Id), AttachedDocuments = attachedDocuments, InvitedMemberIds = members.Select(x => x.Id) });
    });
    public Task SubmitEvaluation(int id, List<ScoreInput> scores, ClaimsPrincipal principal)
    {
        DateTime? limit = null;
        return Change(id, principal, "EvaluationSubmitted", async (t, a) =>
        {
            var member = await Member(t, a, CommitteeType.Evaluation); var s = await Session(id); limit = s.EndsAt;
            Require(s.State == SessionState.Scheduled && Now >= s.StartsAt && Now < s.EndsAt, "Evaluation is not open. Check the scheduled opening and closing times.");
            Require(s.Submissions.All(x => x.CommitteeMemberId != member.Id), "Your final evaluation was already submitted and is immutable.");
            Require(scores.Count == s.EligibleBids.Count && scores.Select(x => x.BidId).Distinct().Count() == scores.Count && scores.All(x => s.EligibleBids.Any(y => y.BidId == x.BidId)), "Score every eligible supplier exactly once.");
            foreach (var score in scores)
            {
                Require(score.Financial.HasValue && score.Technical.HasValue, "Both financial and technical scores must be explicitly supplied, including a deliberate zero score.");
                WeightedCalculation.Range(score.Financial.Value); WeightedCalculation.Range(score.Technical.Value);
                Require(!string.IsNullOrWhiteSpace(score.Justification), "Every supplier evaluation requires justification.");
                foreach (var criterion in s.CommitteeRound.Criteria) db.EvaluationScores.Add(new EvaluationScore { BidId = score.BidId, CommitteeMemberId = member.Id, EvaluationCriterionId = criterion.Id, Score = criterion.Type == CriterionType.Financial ? score.Financial.Value : score.Technical.Value, Comment = score.Justification, ScoredAt = Now });
            }
            var participation = s.Invitations.SingleOrDefault(x => x.CommitteeMemberId == member.Id);
            Require(participation != null, "A persisted evaluation invitation is required.");
            participation.SubmittedAt = Now;
            // Touch rowversion so closure and final submission share the same concurrency boundary.
            db.Entry(s).Property(x => x.State).IsModified = true;
        }, () => limit);
    }
    public Task EndEvaluation(int id, bool confirmed, string reason, ClaimsPrincipal principal) => Change(id, principal, "EvaluationClosed", async (t, a) =>
    {
        await Officer(t, a); var s = await Session(id);
        if (s.ReportId != null && s.State == SessionState.ReportDraft) return;
        Require(s.State == SessionState.Scheduled && confirmed && !string.IsNullOrWhiteSpace(reason), "Confirm closure with a reason. Submitted scores will remain immutable.");
        Require(s.Submissions.Count == 4, "All four final evaluations are required before End Evaluation can finalize scoring. Missing evaluations remain blocked.");
        Closed(s, a.Id, reason);
        await GenerateReportCore(t, a, s, "Automatically calculated from all four independent evaluations and the approved locked weights. " + reason);
    });
    public async Task<IReadOnlyList<RankedBid>> Ranking(EvaluationSession s)
    {
        Require(s.Submissions.Count == 4, "Incomplete evaluation: all four submissions are required. A resolution policy is required.");
        var scores = await db.EvaluationScores.Include(x => x.EvaluationCriterion).Where(x => x.EvaluationCriterion.CommitteeRoundId == s.CommitteeRoundId).ToListAsync();
        var results = new List<(int, decimal)>();
        foreach (var bid in s.EligibleBids)
        {
            var memberScores = new List<decimal>();
            foreach (var submission in s.Submissions)
            {
                var pair = scores.Where(x => x.BidId == bid.BidId && x.CommitteeMemberId == submission.CommitteeMemberId).ToArray();
                Require(pair.Length == 2, "A financial or technical score is missing.");
                memberScores.Add(WeightedCalculation.Member(pair.Single(x => x.EvaluationCriterion.Type == CriterionType.Financial).Score,
                    pair.Single(x => x.EvaluationCriterion.Type == CriterionType.Technical).Score, s.WeightRound.ApprovedFinancial!.Value, s.WeightRound.ApprovedTechnical!.Value));
            }
            results.Add((bid.BidId, WeightedCalculation.Final(memberScores)));
        }
        return WeightedCalculation.Rank(results);
    }
    public Task GenerateReport(int id, string justification, ClaimsPrincipal principal) => Change(id, principal, "UnifiedReportGenerated", async (t, a) =>
    {
        await Member(t, a, CommitteeType.Evaluation); var s = await Session(id);
        if (s.State == SessionState.Scheduled && Now >= s.EndsAt) Closed(s);
        Require(s.State == SessionState.Closed && s.ReportId == null && !string.IsNullOrWhiteSpace(justification), "Close a complete evaluation first and provide the recommendation justification.");
        await GenerateReportCore(t, a, s, justification);
    });
    private async Task GenerateReportCore(Tender t, ApplicationUser a, EvaluationSession s, string justification)
    {
        var ranking = await Ranking(s);
        var scores = await db.EvaluationScores.Where(x => x.EvaluationCriterion.CommitteeRoundId == s.CommitteeRoundId).Select(x => new { x.CommitteeMemberId, x.BidId, x.EvaluationCriterionId, x.Score, x.Comment, x.ScoredAt }).ToListAsync();
        var financialCriterionId = s.CommitteeRound.Criteria.Single(x => x.Type == CriterionType.Financial).Id;
        var memberResults = scores.GroupBy(x => new { x.CommitteeMemberId, x.BidId }).Select(group => new
        {
            group.Key.CommitteeMemberId, group.Key.BidId,
            FinancialScore = group.Single(x => x.EvaluationCriterionId == financialCriterionId).Score,
            TechnicalScore = group.Single(x => x.EvaluationCriterionId != financialCriterionId).Score,
            WeightedMemberScore = WeightedCalculation.Member(group.Single(x => x.EvaluationCriterionId == financialCriterionId).Score,
                group.Single(x => x.EvaluationCriterionId != financialCriterionId).Score, s.WeightRound.ApprovedFinancial!.Value, s.WeightRound.ApprovedTechnical!.Value)
        }).ToArray();
        var bids = await Submitted(s.TenderRoundId); var ai = await db.Set<AiAnalysis>().FindAsync(s.AiAnalysisId);
        var report = new TenderReport { TenderRoundId = s.TenderRoundId, ReportType = ReportType.FullEvaluation, CreatedByUserId = a.Id, CreatedAt = Now,
            RecommendedBidId = ranking.Count(x => x.Rank == 1) == 1 ? ranking.Single(x => x.Rank == 1).BidId : null,
            RankingSnapshotJson = JsonSerializer.Serialize(ranking),
            Content = JsonSerializer.Serialize(new { Tender = new { t.Id, t.Title, t.Description, t.PurchaseRequestId }, Requirements = s.WeightRound.RequirementsJson,
                Weights = new { s.WeightRound.Version, s.WeightRound.ApprovedFinancial, s.WeightRound.ApprovedTechnical, s.WeightRound.ApprovedByUserId, s.WeightRound.ApprovedAt },
                Scores = scores, MemberResults = memberResults, Ranking = ranking, Compliance = bids.Select(x => new { x.Id, x.ComplianceStatus, x.ComplianceNotes, x.MissingRequirements }), AI = new { ai!.Id, ai.Model, ai.Findings, ai.EvidenceJson, ai.ReviewedAt }, Justification = justification,
                TieBlocker = ranking.Count(x => x.Rank == 1) > 1 ? "Exact highest-score tie: documented resolution policy required before award." : null }) };
        s.Report = report; s.State = SessionState.ReportDraft; s.CommitteeRound.TenderReport = report;
        await db.SaveChangesAsync();
        await reportDocuments.Prepare(report, s);
        foreach (var member in await Members(t.Id, CommitteeType.Evaluation)) Notify(member.UserId, t.Id, "Evaluation report ready for signing", "The system calculated all four submissions and generated the PDF. Open the report and sign your assigned section.");
        Audit(a, t.Id, "AutomaticEvaluationReportGenerated", new { report.Id, report.VersionNumber, report.PdfSha256 });
    }
    public Task FinalizeReport(int id, string comment, ClaimsPrincipal principal, string? documentHash = null, bool confirmed = false, byte[]? signatureImage = null) => Change(id, principal, "ReportFinalizationVote", async (t, a) =>
    {
        var member = await Member(t, a, CommitteeType.Evaluation); var s = await Session(id);
        var signingRole = member.Responsibility == CommitteeResponsibility.SourcingManager && await Role(a, SystemRoles.SourcingManager) ? SystemRoles.SourcingManager : SystemRoles.EvaluationCommittee;
        if (s.Report != null && await db.Set<ReportSignature>().AnyAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Evaluation && x.UserId == a.Id))
        { await reportDocuments.Sign(s.Report, s, a, ReportSigningStage.Evaluation, signingRole, member.Responsibility.ToString()!, "Approve", documentHash, confirmed, Now, signatureImage); return; }
        Require(s.State == SessionState.ReportDraft && s.Report != null, "A complete unified report must be available for review.");
        await reportDocuments.Sign(s.Report, s, a, ReportSigningStage.Evaluation, signingRole, member.Responsibility.ToString()!, "Approve", documentHash, confirmed, Now, signatureImage);
        var reviewRoundId = s.ReportReviewRoundId ?? s.CommitteeRoundId;
        var votes = await db.CommitteeVotes.Where(x => x.CommitteeRoundId == reviewRoundId).ToListAsync();
        Require(votes.All(x => x.CommitteeMemberId != member.Id), "You have already finalized this report version.");
        db.CommitteeVotes.Add(new CommitteeVote { CommitteeRoundId = reviewRoundId, CommitteeMemberId = member.Id, Decision = VoteDecision.Approve, Comment = comment, VotedAt = Now });
        if (votes.Count == 3) { s.State = SessionState.Finalized; s.Report!.Status = ReportStatus.Submitted; s.Report.SubmittedAt = Now;
            if (s.ReportReviewRoundId is int revisionRoundId) { var revisionRound = await db.CommitteeRounds.FindAsync(revisionRoundId); revisionRound!.Status = CommitteeRoundStatus.Approved; revisionRound.CompletedAt = Now; }
            else { s.CommitteeRound.Status = CommitteeRoundStatus.Approved; s.CommitteeRound.CompletedAt = Now; }
            t.Status = TenderStatus.OfficerReview; await NotifyOfficer(id, "Unified report finalized", "All four committee members finalized the same report. Forward it to the Sourcing Manager."); }
    });
    // Compatibility entry point: correction is allowed only through a recorded revision assignment.
    public Task ReviseReport(int id, string correction, ClaimsPrincipal principal) => Change(id, principal, "ReportCorrectionVersionCreated", async (t, a) =>
    {
        var session = await Session(id);
        var revision = await db.Set<ReportRevision>().SingleOrDefaultAsync(x => x.TenderReportId == session.ReportId && x.Status == RevisionStatus.AwaitingCorrection);
        Require(revision != null, "An authorized, recorded revision request is required before creating a correction version.");
        await ResubmitRevisionCore(t, a, revision.Id, correction);
    });
    public Task Forward(int id, ClaimsPrincipal principal) => Change(id, principal, "ReportForwarded", async (t, a) =>
    {
        await Officer(t, a); var s = await Session(id);
        Require(s.State == SessionState.Finalized && t.Status == TenderStatus.OfficerReview, "Only finalized reports may be forwarded.");
        await reportDocuments.RequireCommitteeSignatures(s.Report!, s);
        t.Status = TenderStatus.ManagerReview;
        var manager = (await Members(id, CommitteeType.Evaluation)).Single(x => x.Responsibility == CommitteeResponsibility.SourcingManager);
        Notify(manager.UserId, id, "Evaluation report for review", "Review the finalized recommendation and approve, reject, or return with reasons.");
    });
    private TenderApprovalPolicy FinalPolicy(EvaluationSession s)
    {
        var policy = s.FinalApprovalPolicyJson == null ? Policy : JsonSerializer.Deserialize<TenderApprovalPolicy>(s.FinalApprovalPolicyJson)!;
        Require(policy.FinalConfigured, "Awaiting Final Approval Policy: define scope, quorum within the four-member committee, approval threshold, tie outcome and rejection rule.");
        s.FinalApprovalPolicyJson ??= JsonSerializer.Serialize(policy);
        return policy;
    }
    private bool RequiresTop(Tender t, EvaluationSession s)
    {
        var policy = FinalPolicy(s);
        if (policy.TopCommitteeRequirement == "ThresholdOrCritical") Require(t.Currency == policy.ThresholdCurrency, "Threshold currency mismatch: configure an explicit conversion policy before approval.");
        return policy.TopCommitteeRequirement == "All" || policy.TopCommitteeRequirement == "ThresholdOrCritical" && (t.IsCritical || t.EstimatedValue >= policy.HighValueThreshold);
    }
    public Task ManagerDecision(int id, string decision, string comment, ClaimsPrincipal principal, string? documentHash = null, bool confirmed = false, byte[]? signatureImage = null) =>
        decision == "Return" ? RequestManagerReturn(id, comment, principal, documentHash) : Change(id, principal, "ManagerDecision", async (t, a) =>
    {
        var s = await Session(id);
        Require(await Role(a, SystemRoles.SourcingManager) && await db.CommitteeMembers.AnyAsync(x => x.TenderId == id && x.UserId == a.Id && x.Responsibility == CommitteeResponsibility.SourcingManager), "Assigned Sourcing Manager authority required.");
        if (decision == "Approve" && s.Report != null && await db.Set<ReportSignature>().AnyAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Manager && x.UserId == a.Id))
        { await reportDocuments.Sign(s.Report, s, a, ReportSigningStage.Manager, SystemRoles.SourcingManager, "Manager", "Approve", documentHash, confirmed, Now, signatureImage); return; }
        Require(t.Status == TenderStatus.ManagerReview && s.State == SessionState.Finalized && s.Report!.ReviewedAt == null, "This report is not awaiting a manager decision.");
        await reportDocuments.RequireCommitteeSignatures(s.Report!, s);
        if (decision == "Approve") await reportDocuments.Sign(s.Report!, s, a, ReportSigningStage.Manager, SystemRoles.SourcingManager, "Manager", "Approve", documentHash, confirmed, Now, signatureImage);
        Require(decision is "Approve" or "Reject" or "Return", "Unknown decision.");
        Require(decision == "Approve" || !string.IsNullOrWhiteSpace(comment), "Rejection and return require explanatory comments.");
        s.Report!.ReviewedAt = Now; s.Report.ReviewedByUserId = a.Id; s.Report.ReviewComment = comment;
        if (decision == "Return") { s.State = SessionState.Returned; s.Report.Status = ReportStatus.Returned; t.Status = TenderStatus.OfficerReview; }
        else if (decision == "Reject") { s.State = SessionState.Rejected; s.Report.Status = ReportStatus.Rejected; t.Status = TenderStatus.Rejected; }
        else
        {
            s.Report.Status = ReportStatus.Accepted; s.State = SessionState.ManagerApproved;
            // Policy may be pending at manager review; never infer a final-award rule.
            if (Policy.FinalConfigured && RequiresTop(t, s))
            {
                t.Status = TenderStatus.TopCommitteeReview;
                var assignedTop = await Members(id, CommitteeType.Top);
                if (assignedTop.Count == 0)
                {
                    // The Top Committee is a standing body: when the Manager approves, the first four eligible active members are seated automatically (no officer step).
                    var evaluators = await db.CommitteeMembers.Where(x => x.TenderId == id).Select(x => x.UserId).ToListAsync();
                    var pool = new List<ApplicationUser>();
                    foreach (var person in (await users.GetUsersInRoleAsync(SystemRoles.TopCommittee)).Where(x => x.IsActive && !evaluators.Contains(x.Id)).OrderBy(x => x.FullName))
                        if (!await Role(person, SystemRoles.SourcingOfficer) && !await Role(person, SystemRoles.SourcingManager)) pool.Add(person);
                    if (pool.Count >= 4)
                    {
                        foreach (var person in pool.Take(4)) db.CommitteeMembers.Add(new CommitteeMember { TenderId = id, UserId = person.Id, CommitteeType = CommitteeType.Top, AssignedAt = Now });
                        await db.SaveChangesAsync(); assignedTop = await Members(id, CommitteeType.Top);
                    }
                }
                if (assignedTop.Count == 4 && !await db.CommitteeRounds.AnyAsync(x => x.TenderRoundId == s.TenderRoundId && x.CommitteeType == CommitteeType.Top && x.TenderReportId == s.ReportId))
                {
                    var nextRound = (await db.CommitteeRounds.Where(x => x.TenderRoundId == s.TenderRoundId && x.CommitteeType == CommitteeType.Top).MaxAsync(x => (int?)x.RoundNumber) ?? 0) + 1;
                    db.CommitteeRounds.Add(new CommitteeRound { TenderRoundId = s.TenderRoundId, CommitteeType = CommitteeType.Top, RoundNumber = nextRound, TenderReportId = s.ReportId, StartedAt = Now });
                    foreach (var member in assignedTop) Notify(member.UserId, id, "Final decision requested", "Review the signed report and the manager approval, then vote and sign.");
                }
            }
            else if (Policy.FinalConfigured) await RecordFinalDecision(t, s, 0, 0, true);
        }
        Audit(a, id, "ManagerDecisionEvidence", new { decision, comment, s.ReportId });
        await NotifyOfficer(id, "Manager decision", $"{decision}: {comment}");
    });
    public Task AssignTop(int id, string[] memberIds, ClaimsPrincipal principal) => Change(id, principal, "TopCommitteeAssigned", async (t, a) =>
    {
        await Officer(t, a); var s = await Session(id);
        Require(s.State == SessionState.ManagerApproved && RequiresTop(t, s), "Manager approval and a configured policy requiring Top Committee are required.");
        Require(memberIds.Length == 4 && memberIds.Distinct().Count() == 4 && !await db.CommitteeMembers.AnyAsync(x => x.TenderId == id && x.CommitteeType == CommitteeType.Top), "Assign four distinct Top Committee members once.");
        foreach (var userId in memberIds)
        {
            var person = await users.FindByIdAsync(userId);
            Require(person is { IsActive: true } && await Role(person, SystemRoles.TopCommittee) && !await Role(person, SystemRoles.SourcingOfficer) && !await Role(person, SystemRoles.SourcingManager) && !await db.CommitteeMembers.AnyAsync(x => x.TenderId == id && x.UserId == userId), "Top members must be active, appropriately authorized, and separate from the Evaluation Committee, officer, and manager.");
            db.CommitteeMembers.Add(new CommitteeMember { TenderId = id, UserId = userId, CommitteeType = CommitteeType.Top, AssignedAt = Now });
            Notify(userId, id, "Final decision requested", "Review the finalized report, recommendation, evidence and manager decision. Do not re-enter evaluation scores.");
        }
        db.CommitteeRounds.Add(new CommitteeRound { TenderRoundId = s.TenderRoundId, CommitteeType = CommitteeType.Top, RoundNumber = 1, TenderReportId = s.ReportId, StartedAt = Now });
        t.Status = TenderStatus.TopCommitteeReview;
    });
    public Task TopVote(int id, VoteDecision decision, string comment, ClaimsPrincipal principal, string? documentHash = null, bool confirmed = false, byte[]? signatureImage = null) => Change(id, principal, "TopCommitteeDecision", async (t, a) =>
    {
        var member = await Member(t, a, CommitteeType.Top); var s = await Session(id);
        var topSeats = await db.CommitteeMembers.Where(x => x.TenderId == id && x.CommitteeType == CommitteeType.Top).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync();
        var slot = (topSeats.IndexOf(member.Id) + 1).ToString();
        if (s.Report != null && await db.Set<ReportSignature>().AnyAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Top && x.UserId == a.Id))
        { await reportDocuments.Sign(s.Report, s, a, ReportSigningStage.Top, SystemRoles.TopCommittee, slot, decision.ToString(), documentHash, confirmed, Now, signatureImage); return; }
        Require(s.State == SessionState.ManagerApproved && t.Status == TenderStatus.TopCommitteeReview && RequiresTop(t, s), "This recommendation is not awaiting Top Committee decisions.");
        Require(Enum.IsDefined(decision) && !string.IsNullOrWhiteSpace(comment), "An official decision and justification are required.");
        await reportDocuments.RequireCommitteeSignatures(s.Report!, s);
        Require(await db.Set<ReportSignature>().AnyAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Manager && x.Decision == "Approve" && x.InvalidatedAt == null), "Current-version manager signing is required before Top voting.");
        await reportDocuments.Sign(s.Report!, s, a, ReportSigningStage.Top, SystemRoles.TopCommittee, slot, decision.ToString(), documentHash, confirmed, Now, signatureImage);
        var cr = await db.CommitteeRounds.SingleAsync(x => x.TenderRoundId == s.TenderRoundId && x.CommitteeType == CommitteeType.Top && x.TenderReportId == s.ReportId);
        Require(!await db.CommitteeVotes.AnyAsync(x => x.CommitteeRoundId == cr.Id && x.CommitteeMemberId == member.Id), "Your final decision is already recorded.");
        db.CommitteeVotes.Add(new CommitteeVote { CommitteeRoundId = cr.Id, CommitteeMemberId = member.Id, Decision = decision, Comment = comment, VotedAt = Now });
        var priorVotes = await db.CommitteeVotes.Where(x => x.CommitteeRoundId == cr.Id).Select(x => x.Decision).ToListAsync();
        var approvesNow = priorVotes.Count(d => d == VoteDecision.Approve) + (decision == VoteDecision.Approve ? 1 : 0);
        if (approvesNow >= FinalPolicy(s).TopApprovalsRequired) await RecordFinalDecision(t, s, approvesNow, priorVotes.Count + 1, false);
        Audit(a, id, "TopDecisionEvidence", new { decision, comment, s.ReportId, PolicyVersion = FinalPolicy(s).Version });
    });
    public Task Award(int id, ClaimsPrincipal principal) => Change(id, principal, "TenderAwardedAndClosed", async (t, a) =>
    {
        await Officer(t, a); var s = await Session(id); var top = RequiresTop(t, s); var finalPolicy = FinalPolicy(s);
        // Chain of custody: Top approvals -> assigned Manager acknowledges and forwards -> Officer. Legacy non-Top tenders get their notice here.
        var relay = await db.Set<FinalDecisionNotice>().FirstOrDefaultAsync(x => x.TenderId == id && x.TenderReportId == s.ReportId);
        if (relay == null && !top) await RecordFinalDecision(t, s, 0, 0, true);
        else Require(relay != null && relay.Status != FinalDecisionStatus.AwaitingManager, "The assigned Sourcing Manager must acknowledge and forward the Top Committee decision before the tender can be awarded.");
        Require(s.State == SessionState.ManagerApproved && s.Report?.Status == ReportStatus.Accepted && t.AwardedBidId == null && t.Status is not (TenderStatus.Rejected or TenderStatus.Cancelled), "Final manager approval is required; rejected or returned recommendations cannot be awarded.");
        await reportDocuments.RequireCommitteeSignatures(s.Report, s);
        Require(await db.Set<ReportSignature>().AnyAsync(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Manager && x.Decision == "Approve" && x.InvalidatedAt == null), "The manager must sign the current generated report before award.");
        var ranking = await Ranking(s);
        Require(ranking.Count(x => x.Rank == 1) == 1 && s.Report.RecommendedBidId == ranking.Single(x => x.Rank == 1).BidId, "Exact top-score ties require a documented resolution policy before award.");
        var bid = await db.Bids.SingleAsync(x => x.Id == s.Report.RecommendedBidId);
        Require(bid.ComplianceStatus == ComplianceStatus.Compliant && bid.Status == BidStatus.Submitted, "Only eligible compliant offers can be awarded.");
        Require(await db.TenderInvitations.AnyAsync(x => x.Id == bid.TenderInvitationId && x.Supplier.Status == SupplierStatus.Approved), "The recommended supplier is no longer eligible for award.");
        var votes = await db.CommitteeVotes.Include(x => x.CommitteeMember).Where(x => x.CommitteeRound.TenderRoundId == s.TenderRoundId && x.CommitteeRound.CommitteeType == CommitteeType.Top && x.CommitteeRound.TenderReportId == s.ReportId).ToListAsync();
        if (top)
        {
            var members = await Members(id, CommitteeType.Top);
            var signatures = await db.Set<ReportSignature>().Where(x => x.TenderReportId == s.ReportId && x.Stage == ReportSigningStage.Top && x.InvalidatedAt == null).ToListAsync();
            Require(signatures.Count == votes.Count && votes.All(v => signatures.Any(signature => signature.UserId == v.CommitteeMember.UserId && signature.Decision == v.Decision.ToString())), "Every counted Top decision must have a verified current-version signature.");
            var approves = votes.Count(x => x.Decision == VoteDecision.Approve);
            var rejects = votes.Count - approves;
            Require(members.Count == 4 && votes.Count >= finalPolicy.TopQuorum && votes.All(x => members.Any(m => m.Id == x.CommitteeMemberId)) && approves >= finalPolicy.TopApprovalsRequired &&
                (finalPolicy.RejectVoteBlocksAward != true || rejects == 0) && (approves != rejects || finalPolicy.TopTieOutcome == "Approve"), "Configured four-member final approval requirements have not been met, or the configured tie/rejection policy blocks award.");
            var cr = await db.CommitteeRounds.SingleAsync(x => x.TenderRoundId == s.TenderRoundId && x.CommitteeType == CommitteeType.Top && x.TenderReportId == s.ReportId); cr.Status = CommitteeRoundStatus.Approved; cr.CompletedAt = Now;
        }
        t.AwardedBidId = bid.Id; t.AwardedAt = Now; t.ClosedAt = Now; t.Status = TenderStatus.Awarded; s.State = SessionState.Awarded;
        t.AwardedByUserId = a.Id;
        t.AwardApprovalEvidenceJson = JsonSerializer.Serialize(new { EvaluationSessionId = s.Id, Policy = finalPolicy, s.ReportId, s.Report.ReviewedByUserId, s.Report.ReviewedAt, WeightApproval = s.WeightRoundId, Votes = votes.Select(x => new { x.CommitteeMemberId, x.Decision, x.VotedAt }) });
        // Suppliers are deliberately NOT notified here. Only the Officer's "Notify Suppliers" action announces results.
        foreach (var user in (await Members(id, CommitteeType.Evaluation)).Select(x => x.UserId).Append(a.Id).Distinct())
            Notify(user, id, "Tender awarded and closed", "The official supplier selection has completed all configured approval gates. Suppliers are notified separately by the Sourcing Officer.");
    });

    public async Task<bool> CanRead(Tender t, ApplicationUser a)
    {
        if (await Role(a, SystemRoles.Supplier)) return a.SupplierId != null && await db.TenderInvitations.AnyAsync(x => x.TenderRound.TenderId == t.Id && x.SupplierId == a.SupplierId);
        if (await IsOfficer(a)) return true;
        var m = await db.CommitteeMembers.SingleOrDefaultAsync(x => x.TenderId == t.Id && x.UserId == a.Id);
        if (m == null) return false;
        return m.CommitteeType == CommitteeType.Top ? await Role(a, SystemRoles.TopCommittee) : await Role(a, SystemRoles.EvaluationCommittee) || m.Responsibility == CommitteeResponsibility.SourcingManager && await Role(a, SystemRoles.SourcingManager);
    }
    public async Task<List<WorkflowListRow>> List(ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        var supplier = await Role(a, SystemRoles.Supplier);
        var query = db.Tenders.AsNoTracking();
        if (supplier) query = query.Where(x => db.TenderInvitations.Any(i => i.TenderRound.TenderId == x.Id && i.SupplierId == a.SupplierId));
        else if (!await IsOfficer(a)) query = query.Where(x => x.CommitteeMembers.Any(m => m.UserId == a.Id));
        return await query.OrderByDescending(x => x.Id).Select(x => new WorkflowListRow(x.Id, x.Title, x.Status.ToString())).ToListAsync();
    }
    public async Task<WorkflowDetails> Details(int id, ClaimsPrincipal principal)
    {
        var a = await Actor(principal); var t = await db.Tenders.Include(x => x.Items).Include(x => x.Rounds).ThenInclude(x => x.Invitations).Include(x => x.CommitteeMembers).SingleOrDefaultAsync(x => x.Id == id);
        Require(t != null && await CanRead(t, a), "Tender not found or access denied.");
        var supplier = await Role(a, SystemRoles.Supplier);
        if (supplier) { foreach (var r in t!.Rounds) r.Invitations = r.Invitations.Where(x => x.SupplierId == a.SupplierId).ToList(); t.CommitteeMembers = []; }
        var w = await db.Set<WeightRound>().Include(x => x.Proposals).OrderByDescending(x => x.Version).FirstOrDefaultAsync(x => x.TenderId == id);
        var s = supplier ? null : await db.Set<EvaluationSession>().Include(x => x.WeightRound).Include(x => x.CommitteeRound).ThenInclude(x => x.Criteria).Include(x => x.EligibleBids).Include(x => x.Report).Include(x => x.Invitations).Include(x => x.TenderRound).SingleOrDefaultAsync(x => x.TenderRound.TenderId == id && x.TenderRound.RoundNumber == db.TenderRounds.Where(r => r.TenderId == id).Max(r => r.RoundNumber));
        // Deadline is authoritative even without background processing. Persist the automatic closure under the same lock.
        if (s?.State == SessionState.Scheduled && Now >= s.EndsAt)
            await Change(id, principal, "EvaluationAutoClosed", async (tender, _) =>
            {
                var current = await Session(id);
                if (current.State == SessionState.Scheduled && Now >= current.EndsAt)
                {
                    Closed(current);
                    if (current.State == SessionState.Closed) await GenerateReportCore(tender, await GlobalOfficer(), current, "Automatically calculated at the scheduled deadline from all four independent evaluations and approved weights.");
                }
            });
        var officer = !supplier && await IsOfficer(a);
        var evaluator = !supplier && t!.CommitteeMembers.Any(x => x.UserId == a.Id && x.CommitteeType == CommitteeType.Evaluation) && (await Role(a, SystemRoles.EvaluationCommittee) || await Role(a, SystemRoles.SourcingManager));
        var top = !supplier && t!.CommitteeMembers.Any(x => x.UserId == a.Id && x.CommitteeType == CommitteeType.Top) && await Role(a, SystemRoles.TopCommittee);
        var canReadBids = officer || s != null && (evaluator || top);
        if (!canReadBids) s = null; // Weight-approval authority does not confer access to bid reports or AI findings.
        var roundId = t!.Rounds.OrderByDescending(x => x.RoundNumber).FirstOrDefault()?.Id;
        var bids = await db.Bids.Include(x => x.Items).Include(x => x.TenderInvitation).ThenInclude(x => x.Supplier).Where(x => x.TenderInvitation.TenderRoundId == roundId && (supplier ? x.TenderInvitation.SupplierId == a.SupplierId : canReadBids)).ToListAsync();
        var result = new WorkflowDetails { Tender = t, Weights = w, Session = s, Bids = bids, UserId = a.Id, IsOfficer = officer, IsEvaluator = evaluator, IsTop = top, IsSupplier = supplier,
            IsManager = evaluator && await Role(a, SystemRoles.SourcingManager) && t.CommitteeMembers.Any(x => x.UserId == a.Id && x.Responsibility == CommitteeResponsibility.SourcingManager),
            CanApproveWeights = !supplier && await WeightApprover(a) && (await ManagerSeat(id))?.UserId == a.Id && await Role(a, SystemRoles.SourcingManager), WeightPolicyConfigured = Policy.WeightConfigured, FinalPolicyConfigured = s?.FinalApprovalPolicyJson != null || Policy.FinalConfigured, UtcNow = Now };
        if (supplier && w != null) w.Proposals = []; // Supplier sees approved published criteria, never proposals/audit.
        await LoadRoundControl(result, t, a, officer, supplier);
        result.Documents = await db.Documents.Where(x => x.TenderId == id || x.BidId != null && bids.Select(b => b.Id).Contains(x.BidId.Value)).AsNoTracking().ToListAsync();
        if (!supplier)
        {
            result.History = await db.AuditLogs.Where(x => x.EntityName == "Tender" && x.EntityId == id.ToString() &&
                (canReadBids || x.Action == "WeightRequestSent" || x.Action == "WeightProposalSubmitted" || x.Action == "ProposalEvidence" || x.Action == "WeightsApproved")).OrderBy(x => x.CreatedAt).ToListAsync();
            if (canReadBids) result.Analyses = await db.Set<AiAnalysis>().Where(x => x.TenderRoundId == roundId).ToListAsync();
            if (s != null)
            {
                result.Scores = await db.EvaluationScores.Include(x => x.EvaluationCriterion).Where(x => x.EvaluationCriterion.CommitteeRoundId == s.CommitteeRoundId && (s.State != SessionState.Scheduled || x.CommitteeMember.UserId == a.Id)).ToListAsync();
                result.Votes = await db.CommitteeVotes.Include(x => x.CommitteeRound).Where(x => x.CommitteeRound.TenderRoundId == roundId).ToListAsync();
                if (s.Submissions.Count == 4 && s.State != SessionState.Scheduled) result.Ranking = await Ranking(s);
            }
        }
        return result;
    }
    public async Task<(Document Document, Tender Tender)> AuthorizedDocument(int documentId, ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        var d = await db.Documents.Include(x => x.Bid).ThenInclude(x => x!.TenderInvitation).ThenInclude(x => x.TenderRound).Include(x => x.Tender).Include(x => x.TenderReport).ThenInclude(x => x!.TenderRound).SingleOrDefaultAsync(x => x.Id == documentId);
        Require(d != null, "Document not found.");
        var tenderId = d!.TenderId ?? d.Bid?.TenderInvitation.TenderRound.TenderId ?? d.TenderReport?.TenderRound.TenderId;
        Require(tenderId != null, "This document is outside the tender workflow.");
        var tender = await db.Tenders.SingleAsync(x => x.Id == tenderId);
        Require(await CanRead(tender, a), "Document access denied.");
        if (await Role(a, SystemRoles.Supplier)) Require(d.TenderId == tender.Id || d.Bid?.TenderInvitation.SupplierId == a.SupplierId, "Supplier document access denied.");
        else if (d.BidId != null || d.TenderReportId != null)
        {
            var officer = await IsOfficer(a);
            var assignment = await db.CommitteeMembers.SingleOrDefaultAsync(x => x.TenderId == tender.Id && x.UserId == a.Id);
            var committeeAccess = assignment != null && (assignment.CommitteeType == CommitteeType.Top ? await Role(a, SystemRoles.TopCommittee) :
                await Role(a, SystemRoles.EvaluationCommittee) || assignment.Responsibility == CommitteeResponsibility.SourcingManager && await Role(a, SystemRoles.SourcingManager));
            Require(officer || committeeAccess && await db.Set<EvaluationSession>().AnyAsync(x => x.TenderRound.TenderId == tender.Id), "Confidential documents require current committee authority and evaluation setup, or ownership as Sourcing Officer.");
        }
        return (d, tender);
    }
    public Task ReviewAi(int id, int analysisId, ClaimsPrincipal principal) => Change(id, principal, "AiAnalysisReviewed", async (t, a) =>
    {
        await Officer(t, a); var r = await Round(id);
        var ai = await db.Set<AiAnalysis>().SingleOrDefaultAsync(x => x.Id == analysisId && x.TenderRoundId == r.Id);
        Require(ai?.Status == "Completed" && ai.ReviewedAt == null, "Completed, unreviewed analysis required.");
        ai!.ReviewedByUserId = a.Id; ai.ReviewedAt = Now;
    });
    public async Task CloseDueSessions(CancellationToken ct)
    {
        var ids = await db.Set<EvaluationSession>().Where(x => x.State == SessionState.Scheduled && x.EndsAt <= Now).Select(x => x.TenderRound.TenderId).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var tender = await db.Tenders.FromSqlInterpolated($"SELECT * FROM Tenders WITH (UPDLOCK,HOLDLOCK) WHERE Id={id}").SingleAsync(ct);
            var s = await Session(id);
            if (s.State == SessionState.Scheduled && Now >= s.EndsAt)
            {
                Closed(s);
                if (s.State == SessionState.Closed)
                    await GenerateReportCore(tender, await GlobalOfficer(), s, "Automatically calculated from all four independent final evaluations at the scheduled deadline, using approved locked weights.");
                await NotifyOfficer(id, "Evaluation closed at the deadline", $"Scoring closed automatically with {s.Submissions.Count} of 4 final submissions.");
                db.AuditLogs.Add(new AuditLog { EntityName = "Tender", EntityId = id.ToString(), Action = "EvaluationAutoClosed", CreatedAt = Now });
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
    }
    public async Task<List<Notification>> Notifications(ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        return await db.Notifications.AsNoTracking().Where(x => x.UserId == a.Id).OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync();
    }
    public async Task ReadNotifications(ClaimsPrincipal principal)
    {
        var a = await Actor(principal);
        await db.Notifications.Where(x => x.UserId == a.Id && !x.IsRead).ExecuteUpdateAsync(x => x.SetProperty(n => n.IsRead, true));
    }
}
