using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

// Constructed by TopCommittee controllers from existing injected dependencies. No shared DI changes.
public sealed class TopCommitteeService(ApplicationDbContext db, UserManager<ApplicationUser> users)
{
    public IQueryable<CommitteeRound> AssignedRounds(string userId) => db.CommitteeRounds.Where(r =>
        (r.CommitteeType == CommitteeType.Top || r.CommitteeType == TopCommitteeCompatibility.LegacyTop) &&
        r.TenderRound.Tender.CommitteeMembers.Any(m =>
            (m.CommitteeType == CommitteeType.Top || m.CommitteeType == TopCommitteeCompatibility.LegacyTop) && m.UserId == userId));

    public IQueryable<CommitteeRound> RoundList(string userId) => AssignedRounds(userId)
        .Include(r => r.TenderRound).ThenInclude(r => r.Tender)
        .Include(r => r.TenderRound).ThenInclude(r => r.Tender).ThenInclude(t => t.AwardedBid)
            .ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.Supplier)
        .Include(r => r.TenderReport).ThenInclude(r => r!.RecommendedBid)
            .ThenInclude(b => b!.TenderInvitation).ThenInclude(i => i.Supplier)
        .Include(r => r.Votes).ThenInclude(v => v.CommitteeMember).ThenInclude(m => m.User);

    public async Task<RoundDetails?> Load(string userId, int id)
    {
        var round = await RoundList(userId)
            .Include(r => r.TenderReport).ThenInclude(r => r!.CreatedByUser)
            .Include(r => r.TenderRound).ThenInclude(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(p => p.Department)
            .Include(r => r.TenderRound).ThenInclude(r => r.Tender).ThenInclude(t => t.PurchaseRequest).ThenInclude(p => p.Items)
            .AsSplitQuery().SingleOrDefaultAsync(r => r.Id == id);
        if (round is null) return null;
        var tenderId = round.TenderRound.TenderId;
        var members = await db.CommitteeMembers.Include(m => m.User).Where(m => m.TenderId == tenderId).ToListAsync();
        var bids = await db.Bids.Where(b => b.TenderInvitation.TenderRoundId == round.TenderRoundId &&
                b.Status == BidStatus.Submitted && b.SubmittedAt != null &&
                !db.Bids.Any(newer => newer.TenderInvitationId == b.TenderInvitationId &&
                    newer.RevisionNumber > b.RevisionNumber && newer.SubmittedAt != null))
            .Include(b => b.TenderInvitation).ThenInclude(i => i.Supplier)
            .Include(b => b.Items).ThenInclude(i => i.TenderItem).AsSplitQuery().ToListAsync();
        var reports = await db.TenderReports.Where(r => r.TenderRoundId == round.TenderRoundId && r.Status != ReportStatus.Draft)
            .Include(r => r.CreatedByUser).OrderByDescending(r => r.VersionNumber).ToListAsync();
        var evaluations = await db.CommitteeRounds.Where(r => r.TenderRoundId == round.TenderRoundId && r.CommitteeType == CommitteeType.Evaluation)
            .Include(r => r.Votes).Include(r => r.Criteria).ThenInclude(c => c.Scores)
                .ThenInclude(s => s.CommitteeMember).ThenInclude(m => m.User).AsSplitQuery().ToListAsync();
        var topUserIds = members.Where(m => m.CommitteeType == CommitteeType.Top).Select(m => m.UserId).ToArray();
        var managerMembers = members.Where(m => m.CommitteeType == CommitteeType.Evaluation &&
            m.Responsibility == CommitteeResponsibility.SourcingManager).ToList();
        var manager = managerMembers.Count == 1 ? managerMembers[0].User : null;
        if (manager is not null && (!manager.IsActive || !await users.IsInRoleAsync(manager, SystemRoles.SourcingManager))) manager = null;
        var managerId = manager?.Id;
        var threadManagerId = managerId ?? round.TenderReport?.CreatedByUserId;
        var threads = await db.Clarifications.Where(c => c.TenderId == tenderId &&
            ((c.FromUserId == userId && c.ToUserId == threadManagerId) ||
             (c.ToUserId == userId && c.FromUserId == threadManagerId)))
            .Include(c => c.Messages).ThenInclude(m => m.SenderUser)
            .Include(c => c.FromUser).Include(c => c.ToUser).AsSplitQuery().OrderByDescending(c => c.CreatedAt).ToListAsync();
        var supplierIds = bids.Select(b => b.TenderInvitation.SupplierId).Distinct().ToArray();
        var bidIds = bids.Select(b => b.Id).ToArray();
        var reportIds = reports.Select(r => r.Id).ToArray();
        var requestId = round.TenderRound.Tender.PurchaseRequestId;
        var documents = await db.Documents.Where(d => d.TenderId == tenderId || d.PurchaseRequestId == requestId ||
            (d.BidId.HasValue && bidIds.Contains(d.BidId.Value)) ||
            (d.SupplierId.HasValue && supplierIds.Contains(d.SupplierId.Value)) ||
            (d.TenderReportId.HasValue && reportIds.Contains(d.TenderReportId.Value)))
            .AsNoTracking().OrderBy(d => d.FileName).ToListAsync();
        var details = new RoundDetails
        {
            Round = round, Members = members, Bids = bids, Reports = reports, Evaluations = evaluations,
            Threads = threads, Documents = documents, Manager = manager, UserId = userId,
            FullReport = reports.FirstOrDefault(r => r.ReportType == ReportType.FullEvaluation),
            FailedAttempts = await db.TenderRounds.CountAsync(r => r.TenderId == tenderId &&
                r.RoundNumber < round.TenderRound.RoundNumber && r.Status == TenderRoundStatus.Failed),
            IsLatest = !await db.TenderRounds.AnyAsync(r => r.TenderId == tenderId && r.RoundNumber > round.TenderRound.RoundNumber) &&
                !await db.CommitteeRounds.AnyAsync(r => r.TenderRoundId == round.TenderRoundId &&
                    (r.CommitteeType == CommitteeType.Top || r.CommitteeType == TopCommitteeCompatibility.LegacyTop) && r.RoundNumber > round.RoundNumber) &&
                !await db.TenderReports.AnyAsync(r => r.TenderRoundId == round.TenderRoundId && r.ReportType == ReportType.ExecutiveSummary &&
                    r.SubmittedAt != null && r.VersionNumber > (round.TenderReport == null ? 0 : round.TenderReport.VersionNumber)),
            HasOpenClarifications = await db.Clarifications.AnyAsync(c => c.TenderId == tenderId && c.Status != ClarificationStatus.Closed &&
                ((c.FromUserId == managerId && topUserIds.Contains(c.ToUserId)) || (c.ToUserId == managerId && topUserIds.Contains(c.FromUserId))))
        };
        details.Blockers = TopCommitteePolicy.Blockers(details);
        return details;
    }

    // All mutation paths take the same SQL Server transaction-owned lock for the tender.
    // This serializes voting, clarification requests and fourth-vote finalization without schema changes.
    private async Task<int> TenderId(string userId, int roundId) =>
        await AssignedRounds(userId).Where(r => r.Id == roundId)
            .Select(r => (int?)r.TenderRound.TenderId).SingleOrDefaultAsync()
            ?? throw new InvalidOperationException("The committee round is unavailable.");

    private async Task LockTender(int tenderId)
    {
        var resource = $"Orange:TopCommittee:Tender:{tenderId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 51000, 'Committee operation is busy. Retry.', 1;");
    }

    public async Task Vote(string userId, VoteInput input)
    {
        if (input.Decision is null || !Enum.IsDefined(input.Decision.Value) ||
            (input.Decision == VoteDecision.Reject && string.IsNullOrWhiteSpace(input.Comment)) || input.Comment?.Length > 2000)
            throw new InvalidOperationException("Select a valid decision and give a reason when rejecting.");
        var tenderId = await TenderId(userId, input.RoundId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockTender(tenderId);
        db.ChangeTracker.Clear();
        var details = await Load(userId, input.RoundId) ?? throw new InvalidOperationException("Round not found.");
        if (!details.CanVote) throw new InvalidOperationException(details.MyVote is null ? string.Join(" ", details.Blockers) : "Your vote is already recorded and is read-only.");
        var member = details.TopMembers.Single(m => m.UserId == userId);
        var vote = new CommitteeVote
        {
            CommitteeRoundId = details.Round.Id, CommitteeMemberId = member.Id, CommitteeMember = member,
            Decision = input.Decision.Value, Comment = input.Comment?.Trim(), VotedAt = DateTime.UtcNow
        };
        details.Round.Votes.Add(vote);
        Audit(userId, "TopCommitteeVote", "CommitteeRound", details.Round.Id,
            new { Member = member.Id, vote.Decision, vote.Comment, vote.VotedAt });
        var outcome = TopCommitteePolicy.Outcome(details.TopMembers, details.Round.Votes.ToList());
        if (outcome != CommitteeRoundStatus.InProgress)
        {
            var now = DateTime.UtcNow;
            // Exactly one final notification, addressed to the verified responsible Manager only.
            db.Notifications.Add(TopCommitteePolicy.Complete(details, userId, now));
            Audit(userId, "TopCommitteeFinalized", "CommitteeRound", details.Round.Id,
                new { Outcome = outcome, details.Tender.AwardedBidId, Date = now, Manager = details.Manager!.Id });
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    public async Task Clarify(string userId, int roundId, QuestionInput? question, ReplyInput? reply, bool resolve = false)
    {
        var tenderId = await TenderId(userId, roundId);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await LockTender(tenderId);
        db.ChangeTracker.Clear();
        var details = await Load(userId, roundId) ?? throw new InvalidOperationException("Round not found.");
        if (!details.CanClarify) throw new InvalidOperationException("Clarifications require a current active round and its responsible Manager.");
        Clarification thread;
        var now = DateTime.UtcNow;
        if (question is not null)
        {
            thread = new Clarification { TenderId = details.Tender.Id, FromUserId = userId, ToUserId = details.Manager!.Id,
                Subject = question.Subject.Trim(), CreatedAt = now, Status = ClarificationStatus.Open };
            db.Clarifications.Add(thread);
        }
        else
        {
            thread = details.Threads.SingleOrDefault(c => c.Id == reply?.ThreadId &&
                ((c.FromUserId == userId && c.ToUserId == details.Manager!.Id) ||
                 (c.ToUserId == userId && c.FromUserId == details.Manager!.Id)))
                ?? throw new InvalidOperationException("This clarification is unavailable.");
            if (thread.Status == ClarificationStatus.Closed) throw new InvalidOperationException("This thread is already resolved.");
        }
        if (resolve)
        {
            if (thread.Status != ClarificationStatus.Answered || thread.Messages.OrderByDescending(m => m.SentAt).FirstOrDefault()?.SenderUserId != details.Manager!.Id)
                throw new InvalidOperationException("The Manager must answer before a clarification can be resolved.");
            thread.Status = ClarificationStatus.Closed;
            thread.ClosedAt = now;
        }
        else
        {
            var message = (question?.Message ?? reply?.Message)?.Trim();
            if (string.IsNullOrWhiteSpace(message) || message.Length > 10000) throw new InvalidOperationException("Enter a valid message.");
            thread.Messages.Add(new ClarificationMessage { SenderUserId = userId, Message = message, SentAt = now });
            thread.Status = ClarificationStatus.Open;
            db.Notifications.Add(new Notification
            {
                UserId = details.Manager!.Id, Title = "Top Committee — Clarification",
                Message = $"Tender #{details.Tender.Id}: {thread.Subject}. A committee member requested additional details.",
                Link = $"/SourcingManager/Reports/Review/{details.FullReport?.Id ?? details.Round.TenderReportId}", CreatedAt = now
            });
        }
        Audit(userId, resolve ? "TopClarificationResolved" : "TopClarificationMessage", "Tender", details.Tender.Id,
            new { thread.Subject, thread.Status });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private void Audit(string userId, string action, string entity, int id, object values) => db.AuditLogs.Add(new AuditLog
    {
        UserId = userId, Action = action, EntityName = entity, EntityId = id.ToString(),
        NewValues = JsonSerializer.Serialize(values), CreatedAt = DateTime.UtcNow
    });

    public async Task<ManagementStatistics> Statistics()
    {
        var statusGroups = await db.Tenders.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
        // Multiple stored spellings can map to one logical status in this module.
        var distribution = statusGroups.GroupBy(g => g.Status).ToDictionary(g => g.Key, g => g.Sum(row => row.Count));
        var rows = await db.Suppliers.OrderBy(s => s.CompanyName).Select(s => new SupplierStatistic
        {
            SupplierId = s.Id, Name = s.CompanyName, Status = s.Status, Invitations = s.Invitations.Count,
            SubmittedOffers = s.Invitations.Count(i => i.Bids.Any(b => b.SubmittedAt != null))
        }).ToListAsync();
        var awards = await db.Tenders.Where(t => t.Status == TenderStatus.Awarded && t.AwardedBidId != null)
            .Include(t => t.AwardedBid).ThenInclude(b => b!.Items)
            .Include(t => t.AwardedBid).ThenInclude(b => b!.TenderInvitation).AsNoTracking().AsSplitQuery().ToListAsync();
        for (var i = 0; i < rows.Count; i++)
        {
            var supplierAwards = awards.Where(t => t.AwardedBid!.TenderInvitation.SupplierId == rows[i].SupplierId).ToList();
            rows[i].Awards = supplierAwards.Count;
            rows[i].Values = supplierAwards.GroupBy(t => t.AwardedBid!.CurrencyCode)
                .Select(g => new CurrencyValue(g.Key, g.Sum(t => CommitteeDisplay.Total(t.AwardedBid!)))).ToList();
        }
        return new ManagementStatistics
        {
            Suppliers = rows.Count, ApprovedSuppliers = rows.Count(s => s.Status == SupplierStatus.Approved),
            Tenders = distribution.Values.Sum(), ActiveTenders = distribution.Where(g => g.Key != TenderStatus.Awarded && g.Key != TenderStatus.Rejected && g.Key != TenderStatus.Cancelled).Sum(g => g.Value),
            AwardedTenders = awards.Count, CompletedRequests = await db.PurchaseRequests.CountAsync(p => p.Tenders.Any(t => t.Status == TenderStatus.Awarded)),
            Distribution = distribution, SupplierRows = rows,
            Values = awards.GroupBy(t => t.AwardedBid!.CurrencyCode).Select(g => new CurrencyValue(g.Key, g.Sum(t => CommitteeDisplay.Total(t.AwardedBid!)))).ToList()
        };
    }
}

