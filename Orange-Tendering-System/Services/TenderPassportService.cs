using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

/// <summary>
/// Builds the Tender Passport (package, stickers, summary and activity trail) from the existing AuditLogs table.
/// Input is an already authorized <see cref="WorkflowDetails"/>; visibility mirrors TenderWorkflowService.Details:
/// readers without bid access (no evaluation session and not the Officer) only see setup, weight and view events.
/// Supplier accounts never receive a passport. Evidence rows written next to a workflow action (…Evidence, report
/// view/sign receipts) are merged into that action instead of becoming separate stickers.
/// </summary>
public sealed partial class TenderPassportService(ApplicationDbContext db, TimeProvider clock)
{
    public const int MaxStickers = 6;
    private const int TrailLimit = 500;
    private static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(3);

    private static readonly Dictionary<string, (StickerKind Kind, string Label)> Catalog = new(StringComparer.Ordinal)
    {
        ["RequirementsCreated"] = (StickerKind.Created, "Tender created"),
        [AuditLogService.TenderViewed] = (StickerKind.Viewed, "Tender viewed"),
        ["ReportPdfAccessed"] = (StickerKind.Viewed, "Report previewed"),
        [AuditLogService.DocumentViewed] = (StickerKind.Viewed, "Document viewed"),
        [AuditLogService.DocumentDownloaded] = (StickerKind.Downloaded, "Document downloaded"),
        ["ReportPdfDownloaded"] = (StickerKind.Downloaded, "Report downloaded"),
        [AuditLogService.AiAnalysisPdfDownloaded] = (StickerKind.Downloaded, "AI analysis downloaded"),
        ["WeightRequestSent"] = (StickerKind.Submitted, "Sent for weight proposals"),
        ["WeightProposalSubmitted"] = (StickerKind.Submitted, "Weight proposal submitted"),
        ["BidSubmitted"] = (StickerKind.Submitted, "Offer submitted"),
        ["DraftOfferSubmitted"] = (StickerKind.Submitted, "Offer submitted"),
        ["EvaluationSubmitted"] = (StickerKind.Submitted, "Evaluation submitted"),
        ["ReportSentToCommittee"] = (StickerKind.Submitted, "Report sent for signatures"),
        ["ReportForwarded"] = (StickerKind.Submitted, "Report forwarded to the Manager"),
        ["ProceedWithFewerSuppliersRequested"] = (StickerKind.Submitted, "Fewer-offer exception requested"),
        ["SupplierResultsAnnounced"] = (StickerKind.Submitted, "Results announced to suppliers"),
        ["WeightsApproved"] = (StickerKind.Approved, "Weights approved"),
        ["ReportFinalizationVote"] = (StickerKind.Approved, "Report signed"),
        ["ManagerDecision"] = (StickerKind.Reviewed, "Manager decision"),
        ["TopCommitteeDecision"] = (StickerKind.Reviewed, "Top Committee vote"),
        ["ProceedWithFewerSuppliersDecided"] = (StickerKind.Reviewed, "Fewer-offer exception decided"),
        ["TopRejectionFinalized"] = (StickerKind.Rejected, "Top Committee rejection finalized"),
        ["TopRejectionCancellationExecuted"] = (StickerKind.Rejected, "Tender cancelled"),
        ["TopApprovalRelayReplacedByRejection"] = (StickerKind.Rejected, "Top approval stopped"),
        ["ReportReturnedForRevision"] = (StickerKind.Returned, "Returned for correction"),
        ["ReportCorrectionVersionCreated"] = (StickerKind.Corrected, "Correction version created"),
        ["ReportRevisionResubmitted"] = (StickerKind.Corrected, "Correction resubmitted"),
        ["RevisionPolicyAuthorized"] = (StickerKind.Corrected, "Revision authorized"),
        ["TenderDetailsEditedForRetender"] = (StickerKind.Corrected, "Details corrected for re-tender"),
        ["MissingBiddingDeadlineSet"] = (StickerKind.Corrected, "Bidding deadline corrected"),
        ["AiAnalysisReviewed"] = (StickerKind.Reviewed, "AI analysis reviewed"),
        ["ComplianceRecorded"] = (StickerKind.Reviewed, "Compliance reviewed"),
        ["FinalDecisionAcknowledgedByManager"] = (StickerKind.Reviewed, "Final decision acknowledged"),
        ["TopReasonAgreementRecorded"] = (StickerKind.Reviewed, "Rejection reason agreed"),
        ["ProceedToEvaluationPreparation"] = (StickerKind.Reviewed, "Offers reviewed, proceeding"),
        ["TenderAwardedAndClosed"] = (StickerKind.Awarded, "Tender awarded"),
        ["BiddingOpened"] = (StickerKind.Modified, "Bidding opened"),
        ["BiddingClosed"] = (StickerKind.Modified, "Bidding closed"),
        ["SupplierInvited"] = (StickerKind.Modified, "Supplier invited"),
        ["ExternalSupplierInvited"] = (StickerKind.Modified, "External supplier invited"),
        ["InvitationResponded"] = (StickerKind.Modified, "Invitation answered"),
        ["EvaluationScheduled"] = (StickerKind.Modified, "Evaluation scheduled"),
        ["EvaluationRescheduled"] = (StickerKind.Modified, "Evaluation rescheduled"),
        ["EvaluationClosed"] = (StickerKind.Modified, "Evaluation closed"),
        ["EvaluationAutoClosed"] = (StickerKind.Modified, "Evaluation closed automatically"),
        ["EvaluationAutoOpened"] = (StickerKind.Modified, "Evaluation opened automatically"),
        ["UnifiedReportGenerated"] = (StickerKind.Modified, "Report generated"),
        ["AutomaticEvaluationReportGenerated"] = (StickerKind.Modified, "Report generated automatically"),
        ["ReportRegenerated"] = (StickerKind.Modified, "Report regenerated"),
        ["NewTenderRoundCreated"] = (StickerKind.Modified, "New round created"),
        ["TopRetenderRoundPublished"] = (StickerKind.Modified, "New round published"),
        ["AiAnalysisExecuted"] = (StickerKind.Modified, "AI analysis run"),
        ["DocumentUploaded"] = (StickerKind.Modified, "Document uploaded"),
        ["EvaluationCommitteeAssigned"] = (StickerKind.Modified, "Evaluation Committee assigned"),
        ["TopCommitteeAssigned"] = (StickerKind.Modified, "Top Committee assigned"),
    };

    // Mirrors the History filter of TenderWorkflowService.Details for readers without bid access, plus their own views.
    private static readonly HashSet<string> SetupActions = new(StringComparer.Ordinal)
    { "RequirementsCreated", "EvaluationCommitteeAssigned", "WeightRequestSent", "WeightProposalSubmitted", "WeightsApproved", AuditLogService.TenderViewed };

    private static readonly string[] CommentKeys = ["Comment", "Reason", "Justification", "Feedback", "RequiredAction", "Notes", "ClosureReason", "ManagerComment", "DecisionReason"];
    private static readonly string[] RolePriority = [SystemRoles.SourcingOfficer, SystemRoles.SourcingManager, SystemRoles.TopCommittee, SystemRoles.EvaluationCommittee,
        SystemRoles.ChiefExecutiveOfficer, SystemRoles.ChiefFinancialOfficer, SystemRoles.ChiefTechnologyOfficer, SystemRoles.ChiefOperationsOfficer,
        SystemRoles.Finance, SystemRoles.Technical, SystemRoles.DepartmentRepresentative, SystemRoles.RequestingDepartment, SystemRoles.Supplier, SystemRoles.Admin];

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")] private static partial Regex WordBreak();
    private static string Words(string value) => WordBreak().Replace(value, " ");
    public static string Reference(int tenderId) => $"T-{tenderId:0000}";

    public async Task<TenderPassportViewModel?> ForTenderAsync(WorkflowDetails model, string? trailUrl)
    {
        if (model.IsSupplier) return null;
        var t = model.Tender;
        var (events, _, people) = await EventsAsync(model);
        var w = model.Weights; var s = model.Session;
        var now = clock.GetUtcNow().UtcDateTime;

        // One sticker per kind (its latest event). Decisions first, then the most recent activity.
        var decisive = new[] { StickerKind.Awarded, StickerKind.Approved, StickerKind.Rejected, StickerKind.Returned };
        var stickers = events.GroupBy(e => e.Kind).Select(g => g.Last() with { Count = g.Count() })
            .OrderByDescending(e => decisive.Contains(e.Kind)).ThenByDescending(e => e.AtUtc).Take(MaxStickers)
            .OrderByDescending(e => e.AtUtc).ToList();
        var fresh = events.LastOrDefault(e => e.ActorId == model.UserId && e.Kind is not (StickerKind.Viewed or StickerKind.Downloaded) && now - e.AtUtc <= FreshWindow);

        var notice = await db.Set<FinalDecisionNotice>().AsNoTracking().Where(x => x.TenderId == t.Id)
            .OrderByDescending(x => x.Id).Select(x => new { x.Status, x.Outcome }).FirstOrDefaultAsync();
        var state = t.Status switch
        {
            TenderStatus.Awarded => PackageState.Awarded,
            TenderStatus.Rejected or TenderStatus.Cancelled => PackageState.Archived,
            _ when s?.State is SessionState.Returned or SessionState.RevisionBlocked => PackageState.Returned,
            _ when s?.State == SessionState.ManagerApproved && t.Status != TenderStatus.TopCommitteeReview || notice is { Outcome: FinalDecisionOutcome.Award } => PackageState.Approved,
            TenderStatus.Evaluation or TenderStatus.OfficerReview or TenderStatus.ManagerReview or TenderStatus.TopCommitteeReview => PackageState.Inspection,
            TenderStatus.Bidding => PackageState.Sealed,
            _ => PackageState.Open
        };
        string? Seat(CommitteeResponsibility r) { var m = t.CommitteeMembers.FirstOrDefault(x => x.CommitteeType == CommitteeType.Evaluation && x.Responsibility == r); return m == null ? null : people.Name(m.UserId); }
        var manager = Seat(CommitteeResponsibility.SourcingManager);
        string? holder = notice is { Status: FinalDecisionStatus.AwaitingManager } ? Join("Sourcing Manager", manager)
            : notice is { Status: FinalDecisionStatus.ForwardedToOfficer } && t.Status is not (TenderStatus.Awarded or TenderStatus.Cancelled or TenderStatus.Rejected) ? "Sourcing Officer"
            : t.Status switch
            {
                TenderStatus.Draft => w?.State == WeightState.Proposals ? "Evaluation Committee" : "Sourcing Officer",
                TenderStatus.Bidding => model.Tender.Rounds.OrderByDescending(r => r.RoundNumber).FirstOrDefault()?.Status == TenderRoundStatus.Open ? "Invited suppliers" : "Sourcing Officer",
                TenderStatus.Evaluation => s == null ? "Evaluation Committee" : s.State switch
                {
                    SessionState.Scheduled => "Evaluation Committee",
                    SessionState.ReportDraft => "Evaluation Committee",
                    _ => "Sourcing Officer"
                },
                TenderStatus.OfficerReview => "Sourcing Officer",
                TenderStatus.ManagerReview => Join("Sourcing Manager", manager),
                TenderStatus.TopCommitteeReview => "Top Committee",
                TenderStatus.Awarded when notice is { Status: not FinalDecisionStatus.SuppliersNotified } => "Sourcing Officer",
                _ => null
            };
        var round = t.Rounds.Count == 0 ? (int?)null : t.Rounds.Max(r => r.RoundNumber);
        var created = events.FirstOrDefault(e => e.Kind == StickerKind.Created);
        return new TenderPassportViewModel
        {
            EntityName = "Tender", EntityId = t.Id, Reference = Reference(t.Id), Title = t.Title,
            State = state, StateLabel = Words(state.ToString()), Stage = Stage(t.Status),
            Round = round, MaxRounds = model.MaxRounds > 0 ? model.MaxRounds : null,
            Version = $"v{w?.Version ?? 1:00}" + (s?.Report != null ? $" · Report v{s.Report.VersionNumber:00}" : ""),
            CreatedBy = created?.Actor, CreatedAtUtc = created?.AtUtc,
            LastModified = events.LastOrDefault(e => e.Kind is StickerKind.Modified or StickerKind.Corrected or StickerKind.Created),
            PreviousView = events.LastOrDefault(e => e.Action == AuditLogService.TenderViewed && e.ActorId != model.UserId),
            LastReviewed = events.LastOrDefault(e => e.Kind is StickerKind.Reviewed or StickerKind.Approved or StickerKind.Rejected or StickerKind.Returned),
            LastCorrected = events.LastOrDefault(e => e.Kind == StickerKind.Corrected),
            LatestAction = events.LastOrDefault(e => e.Kind is not (StickerKind.Viewed or StickerKind.Downloaded)),
            CurrentHolder = holder,
            CurrentViewer = people.Person(model.UserId, now),
            Stickers = stickers, Overflow = Math.Max(0, events.Count - stickers.Count), TotalEvents = events.Count,
            FreshAuditId = fresh?.AuditId, TrailUrl = trailUrl
        };
    }

    public async Task<PassportTrailViewModel> TrailAsync(WorkflowDetails model)
    {
        TenderWorkflowService.Require(!model.IsSupplier, "The activity trail is internal.");
        var (events, truncated, _) = await EventsAsync(model);
        return new PassportTrailViewModel { Reference = Reference(model.Tender.Id), Title = model.Tender.Title, Events = events, Truncated = truncated };
    }

    public static string Stage(TenderStatus status) => status switch
    {
        TenderStatus.Draft => "Setup",
        TenderStatus.Bidding => "Bidding",
        TenderStatus.Evaluation => "Committee evaluation",
        TenderStatus.OfficerReview => "Weighted results",
        TenderStatus.ManagerReview => "Manager approval",
        TenderStatus.TopCommitteeReview => "Top Committee approval",
        TenderStatus.Awarded => "Awarded",
        _ => "Closed"
    };
    private static string Join(string role, string? name) => name == null ? role : role + " · " + name;

    private async Task<(List<TrackingStickerViewModel> Events, bool Truncated, People People)> EventsAsync(WorkflowDetails model)
    {
        var t = model.Tender; var key = t.Id.ToString();
        var canReadBids = model.IsOfficer || model.Session != null;
        var rows = await db.AuditLogs.AsNoTracking().Where(x => x.EntityName == "Tender" && x.EntityId == key)
            .OrderByDescending(x => x.Id).Take(TrailLimit * 3).ToListAsync();
        var truncated = rows.Count == TrailLimit * 3;
        var reports = canReadBids ? await db.TenderReports.AsNoTracking().Where(r => r.TenderRound.TenderId == t.Id).ToDictionaryAsync(r => r.Id, r => r.VersionNumber) : [];
        var reportKeys = reports.Keys.Select(k => k.ToString()).ToList();
        var receipts = reportKeys.Count == 0 ? [] : await db.AuditLogs.AsNoTracking().Where(x => x.EntityName == "TenderReport" && reportKeys.Contains(x.EntityId)).ToListAsync();

        // Attach evidence rows to the workflow action written in the same operation (same user, within a few seconds).
        var primary = rows.Where(r => !r.Action.EndsWith("Evidence", StringComparison.Ordinal)).ToList();
        var byUser = primary.GroupBy(r => r.UserId ?? "").ToDictionary(g => g.Key, g => g.ToList());
        var attached = new Dictionary<long, List<AuditLog>>();
        foreach (var e in rows.Where(r => r.Action.EndsWith("Evidence", StringComparison.Ordinal)).Concat(receipts))
        {
            if (!byUser.TryGetValue(e.UserId ?? "", out var candidates)) continue;
            var best = candidates.Where(p => Math.Abs((p.CreatedAt - e.CreatedAt).TotalSeconds) <= 5)
                .OrderBy(p => Math.Abs((p.CreatedAt - e.CreatedAt).TotalSeconds)).ThenByDescending(p => p.Id > e.Id).FirstOrDefault();
            if (best == null) continue;
            if (!attached.TryGetValue(best.Id, out var list)) attached[best.Id] = list = [];
            list.Add(e);
        }

        var values = primary.ToDictionary(p => p.Id, p => Values(p, attached.TryGetValue(p.Id, out var ev) ? ev : []));
        bool Visible(AuditLog p) => canReadBids || SetupActions.Contains(p.Action)
            || p.Action is AuditLogService.DocumentDownloaded or AuditLogService.DocumentViewed && values[p.Id].TryGetValue("Scope", out var scope) && scope.ValueKind == JsonValueKind.String && scope.GetString() == "Tender";
        var visible = primary.Where(Visible).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToList();

        var seatUsers = t.CommitteeMembers.Select(m => m.UserId);
        var ids = visible.Select(p => p.UserId).Append(t.CreatedByUserId).Append(model.UserId).Concat(seatUsers).OfType<string>().Distinct().ToList();
        var people = await People.LoadAsync(db, ids, t.CommitteeMembers);
        var rounds = t.Rounds.OrderBy(r => r.RoundNumber).ToList();
        int? RoundAt(DateTime at) => rounds.Count == 0 ? null : (rounds.LastOrDefault(r => r.CreatedAt <= at.AddSeconds(5)) ?? rounds[0]).RoundNumber;

        var events = new List<TrackingStickerViewModel>();
        // Older tenders may predate the RequirementsCreated audit row: their Tenders row still records who created them and when.
        if (!visible.Any(p => p.Action == "RequirementsCreated") && t.CreatedAt != default && !string.IsNullOrEmpty(t.CreatedByUserId))
            events.Add(new TrackingStickerViewModel { AuditId = 0, Kind = StickerKind.Created, Action = "Created", Label = "Tender created", ActorId = t.CreatedByUserId,
                Actor = people.Person(t.CreatedByUserId, t.CreatedAt), AtUtc = t.CreatedAt, Reference = Reference(t.Id), Round = RoundAt(t.CreatedAt) });
        foreach (var p in visible)
        {
            var v = values[p.Id];
            var (kind, label) = Classify(p.Action, v);
            var (before, after) = (Status(p.OldValues), Status(p.NewValues));
            string? version = Int(v, "VersionNumber") is int n ? $"Report v{n:00}"
                : Int(v, "ReportId") is int rid && reports.TryGetValue(rid, out var rv) ? $"Report v{rv:00}"
                : p.Action == "WeightProposalSubmitted" && Int(v, "Version") is int req ? $"Requirements v{req:00}" : null;
            var comments = CommentKeys.Select(k => v.TryGetValue(k, out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()?.Trim() : null)
                .Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            var comment = comments.Count == 0 ? null : string.Join(" · ", comments);
            if (comment?.Length > 400) comment = comment[..400] + "…";
            events.Add(new TrackingStickerViewModel
            {
                AuditId = p.Id, Kind = kind, Action = p.Action, Label = label, ActorId = p.UserId,
                Actor = p.UserId == null ? null : people.Person(p.UserId, p.CreatedAt), AtUtc = p.CreatedAt,
                Reference = Reference(t.Id), Round = RoundAt(p.CreatedAt), Version = version,
                Document = v.TryGetValue("FileName", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null,
                Comment = comment,
                PreviousStatus = before != null && before != after ? Words(before) : null,
                NewStatus = after != null && before != after ? Words(after) : null
            });
        }
        if (events.Count > TrailLimit) { events = events.GetRange(events.Count - TrailLimit, TrailLimit); truncated = true; }
        return (events, truncated, people);
    }

    private static (StickerKind, string) Classify(string action, Dictionary<string, JsonElement> v)
    {
        var decision = v.TryGetValue("decision", out var d) ? d.ValueKind == JsonValueKind.String ? d.GetString()
            : d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var i) ? Enum.GetName(typeof(VoteDecision), i) : null : null;
        switch (action)
        {
            case "ManagerDecision" when decision == "Approve": return (StickerKind.Approved, "Manager approved the report");
            case "ManagerDecision" when decision == "Reject": return (StickerKind.Rejected, "Manager rejected the report");
            case "TopCommitteeDecision" when decision == "Approve": return (StickerKind.Approved, "Top Committee approved");
            case "TopCommitteeDecision" when decision == "Reject": return (StickerKind.Rejected, "Top Committee rejected");
            case "TopCommitteeDecision" when decision == "Retender": return (StickerKind.Rejected, "Re-tender requested");
            case "ProceedWithFewerSuppliersDecided" when v.TryGetValue("Approved", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return ok.ValueKind == JsonValueKind.True ? (StickerKind.Approved, "Fewer-offer exception approved") : (StickerKind.Rejected, "Fewer-offer exception declined");
        }
        if (Catalog.TryGetValue(action, out var known)) return known;
        if (action.StartsWith("SupplierResultEmail", StringComparison.Ordinal)) return (StickerKind.Modified, "Result e-mail " + Words(action["SupplierResultEmail".Length..]).ToLowerInvariant());
        return (StickerKind.Modified, Words(action));
    }

    // Top-level JSON properties of the action and its attached evidence (the action's own values win).
    private static Dictionary<string, JsonElement> Values(AuditLog primary, IEnumerable<AuditLog> evidence)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in new[] { primary.NewValues }.Concat(evidence.Select(e => e.NewValues)))
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in doc.RootElement.EnumerateObject()) if (p.Name != "Status") result.TryAdd(p.Name, p.Value.Clone());
            }
            catch (JsonException) { }
        }
        return result;
    }
    private static int? Int(Dictionary<string, JsonElement> v, string key) => v.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n) ? n : null;
    private static string? Status(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"Status\"", StringComparison.Ordinal)) return null;
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null; }
        catch (JsonException) { return null; }
    }

    /// <summary>Names, roles and units of the people that appear in a passport, resolved in three queries.</summary>
    private sealed class People
    {
        private readonly Dictionary<string, (string Name, string? Department, string? Supplier)> users = [];
        private readonly Dictionary<string, List<string>> roles = [];
        private IReadOnlyCollection<CommitteeMember> seats = [];

        public static async Task<People> LoadAsync(ApplicationDbContext db, List<string> ids, IEnumerable<CommitteeMember> seats)
        {
            var people = new People { seats = seats.ToList() };
            foreach (var u in await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email, Department = u.Department != null ? u.Department.Name : null, Supplier = u.Supplier != null ? u.Supplier.CompanyName : null }).ToListAsync())
                people.users[u.Id] = (string.IsNullOrWhiteSpace(u.FullName) ? u.Email ?? "Unknown user" : u.FullName.Trim(), u.Department, u.Supplier);
            foreach (var r in await (from ur in db.UserRoles join role in db.Roles on ur.RoleId equals role.Id where ids.Contains(ur.UserId) select new { ur.UserId, role.Name }).ToListAsync())
            {
                if (r.Name == null) continue;
                if (!people.roles.TryGetValue(r.UserId, out var list)) people.roles[r.UserId] = list = [];
                list.Add(r.Name);
            }
            return people;
        }
        public string? Name(string userId) => users.TryGetValue(userId, out var u) ? u.Name : null;

        /// <summary>The tender committee seat held at that time is the role at the time of the action; otherwise the account's current role.</summary>
        public PassportPerson Person(string userId, DateTime at)
        {
            var u = users.TryGetValue(userId, out var found) ? found : ("Unknown user", null, null);
            var seat = seats.Where(m => m.UserId == userId && m.AssignedAt <= at.AddSeconds(5)).OrderBy(m => m.CommitteeType).FirstOrDefault();
            if (seat != null)
                return new(u.Name, seat.CommitteeType == CommitteeType.Top ? (seat.ExecutivePosition != null ? seat.ExecutiveTitle : "Top Committee member") : SeatName(seat.Responsibility),
                    seat.CommitteeType == CommitteeType.Top ? "Top Committee" : "Evaluation Committee");
            var role = roles.TryGetValue(userId, out var list) ? RolePriority.FirstOrDefault(list.Contains) ?? list.FirstOrDefault() : null;
            return new(u.Name, role, u.Supplier ?? u.Department);
        }
        private static string SeatName(CommitteeResponsibility? r) => r switch
        {
            CommitteeResponsibility.SourcingManager => "Sourcing Manager",
            CommitteeResponsibility.DepartmentRepresentative => "Department Representative",
            CommitteeResponsibility.FinancialRepresentative => "Financial Member",
            CommitteeResponsibility.TechnicalRepresentative => "Technical Member",
            CommitteeResponsibility.TechnicalRepresentative2 => "Technical Member 2",
            _ => "Committee Member"
        };
    }
}
