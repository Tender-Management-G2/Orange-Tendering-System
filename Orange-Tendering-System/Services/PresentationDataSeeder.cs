using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

internal static class PresentationDataSeeder
{
    private const string DatabaseName = TeamDevDatabase.DefaultName + "_Presentation";
    private const string SupplierEmail = "supplier10@orange-demo.local";
    private const string Prefix = "PRESENTATION ";
    private const string Synthetic = "Fictional procurement data for the presentation.";
    // A placeholder image for synthetic evaluation submissions. It represents no person's signature.
    private const string DemoSignature = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII=";

    public static void RequireDatabase(string connection)
    {
        if (!TeamDevDatabase.IsLocalTeamDatabase(connection) ||
            !DatabaseName.Equals(new SqlConnectionStringBuilder(connection).InitialCatalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Presentation data requires the local {DatabaseName} database.");
    }

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = N'OrangeTenderPresentationSeed', @LockMode = N'Exclusive', @LockTimeout = 30000;
            IF @result < 0 THROW 50001, 'Could not lock presentation seed data.', 1;
            """);

        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var workflow = provider.GetRequiredService<TenderWorkflowService>();
        var policy = provider.GetRequiredService<IOptions<TenderApprovalPolicy>>().Value;
        var now = workflow.Now;
        await DemoDataSeeder.EnsureDemoSuppliersAsync(db, users);
        var supplier = await users.FindByEmailAsync(SupplierEmail)
            ?? throw new InvalidOperationException($"Presentation supplier {SupplierEmail} is missing.");
        var suppliers = await db.Suppliers.Where(s => s.Email == "supplier1@orange-demo.local" ||
            s.Email == "supplier2@orange-demo.local" || s.Email == SupplierEmail)
            .OrderBy(s => s.RegistrationNumber).Select(s => s.Id).ToArrayAsync();
        var officer = await TeamUser(SystemRoles.SourcingOfficer);
        var requester = await TeamUser(SystemRoles.RequestingDepartment);
        var manager = await TeamUser(SystemRoles.SourcingManager);
        var department = await TeamUser(SystemRoles.DepartmentRepresentative);
        var finance = await TeamUser(SystemRoles.Finance);
        var technical = await TeamUser(SystemRoles.Technical);
        var evaluationSeats = new[]
        {
            (manager, CommitteeResponsibility.SourcingManager),
            (department, CommitteeResponsibility.DepartmentRepresentative),
            (finance, CommitteeResponsibility.FinancialRepresentative),
            (technical, CommitteeResponsibility.TechnicalRepresentative)
        };

        var first = await Prepare("01 - Business laptops | Evaluation round 1", "Business laptop", "16 GB RAM, 512 GB SSD, 3-year warranty", 1);
        var bidding = await Prepare("02 - Office printers | Last supplier", "Office printer", "Duplex printing, Ethernet, 2-year warranty", 1, bidding: true);
        var second = await Prepare("03 - Network switches | Evaluation round 2", "Network switch", "24 gigabit ports, managed VLANs, 3-year warranty", 2);
        var top = await Prepare("04 - Backup storage | Top Committee", "Backup storage appliance", "16 TB usable storage with redundant disks", 1, top: true);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation("Tender presentation: round 1 /EvaluationCommittee/Workflow/Meeting/{First}, supplier /Supplier/Offers/Create/{Invitation}, round 2 /EvaluationCommittee/Workflow/Meeting/{Second}, Top Committee /TopCommittee/SignedReports/Review/{Report}",
            first.TenderId, bidding.ActionId, second.TenderId, top.ActionId);

        async Task<ApplicationUser> TeamUser(string role) =>
            await users.FindByEmailAsync(TeamAccountSeeder.Find(role).Email)
                ?? throw new InvalidOperationException($"Missing team account for {role}.");

        async Task<(int TenderId, int ActionId)> Prepare(string name, string product, string specs, int roundNumber, bool bidding = false, bool top = false)
        {
            var title = Prefix + name;
            var existing = await db.Tenders.Include(t => t.Rounds).SingleOrDefaultAsync(t => t.Title == title);
            if (existing != null)
            {
                var originalRound = existing.Rounds.Single(r => r.RoundNumber == roundNumber);
                if (bidding)
                {
                    var invitation = await db.TenderInvitations.Include(i => i.Bids)
                        .SingleAsync(i => i.TenderRoundId == originalRound.Id && i.SupplierId == supplier.SupplierId);
                    var draft = invitation.Bids.SingleOrDefault(b => b.Status == BidStatus.Draft);
                    if (existing.Status == TenderStatus.Bidding && originalRound.Status == TenderRoundStatus.Open &&
                        draft != null && originalRound.Deadline <= now.AddDays(1))
                    {
                        originalRound.Deadline = now.AddDays(7);
                        if (draft.ValidUntil < originalRound.Deadline)
                            draft.ValidUntil = now.AddDays(90).Date;
                    }
                    return (existing.Id, invitation.Id);
                }
                var existingSession = await db.Set<EvaluationSession>().SingleAsync(s => s.TenderRoundId == originalRound.Id);
                return (existing.Id, existingSession.ReportId ?? existing.Id);
            }

            var request = new PurchaseRequest
            {
                Title = title, Justification = $"{Synthetic} Replace end-of-life equipment used by the IT support team.",
                CreatedByUserId = requester.Id, DepartmentId = requester.DepartmentId!.Value, Status = PurchaseRequestStatus.ConvertedToTender,
                EvaluationMethod = TenderEvaluationMethod.Weighted, Priority = RequestPriority.Normal,
                CreatedAt = now.AddDays(-18), SubmittedAt = now.AddDays(-17), NeededBy = now.AddDays(30),
                Items = { new PurchaseRequestItem { Name = product, Specifications = specs, Quantity = 10, Unit = "unit" } }
            };
            var item = new TenderItem { Name = product, Specifications = specs, Quantity = 10, Unit = "unit" };
            var tender = new Tender
            {
                Title = title, Description = request.Justification, Requirements = specs + ". Deliver within 21 calendar days.",
                CreatedByUserId = officer.Id, CreatedAt = now.AddDays(-16), PurchaseRequest = request, Items = { item },
                Status = bidding ? TenderStatus.Bidding : TenderStatus.Evaluation, EstimatedValue = 12000
            };
            foreach (var (user, seat) in evaluationSeats)
                tender.CommitteeMembers.Add(new CommitteeMember { UserId = user.Id, CommitteeType = CommitteeType.Evaluation, Responsibility = seat, AssignedAt = now.AddDays(-15) });
            if (roundNumber == 2)
            {
                var failed = MakeRound(1, TenderRoundStatus.Failed, now.AddDays(-13), now.AddDays(-8));
                // Two submitted offers in the earlier attempt explain why this tender had to be reissued.
                for (var i = 0; i < 2; i++)
                    failed.Invitations.Add(MakeInvitation(suppliers[i], item, i, failed.OpensAt!.Value, BidStatus.Submitted, checkedForCompliance: true));
                failed.Invitations.Add(new TenderInvitation { SupplierId = suppliers[2], Status = InvitationStatus.Declined, InvitedByUserId = officer.Id, InvitedAt = failed.OpensAt!.Value, RespondedAt = failed.OpensAt.Value.AddDays(1) });
                tender.Rounds.Add(failed);
            }
            var round = MakeRound(roundNumber, bidding ? TenderRoundStatus.Open : TenderRoundStatus.SentToEvaluation,
                now.AddDays(bidding ? -2 : -6), now.AddDays(bidding ? 7 : -2));
            tender.Rounds.Add(round);
            for (var i = 0; i < suppliers.Length; i++)
                round.Invitations.Add(MakeInvitation(suppliers[i], item, i, round.OpensAt!.Value,
                    bidding && suppliers[i] == supplier.SupplierId ? BidStatus.Draft : BidStatus.Submitted, checkedForCompliance: !bidding));
            db.Tenders.Add(tender);
            await db.SaveChangesAsync();

            var weights = new WeightRound
            {
                TenderId = tender.Id, Version = 1, State = WeightState.Approved,
                CreatedAt = now.AddDays(-15), ApprovedAt = now.AddDays(-14), ApprovedByUserId = manager.Id,
                FinancialAverage = 40, TechnicalAverage = 60, ApprovedFinancial = 40, ApprovedTechnical = 60,
                RequirementsJson = JsonSerializer.Serialize(new { tender.Title, tender.Description, tender.Requirements, Items = new[] { new { item.Name, item.Specifications, item.Quantity, item.Unit } } }),
                TechnicalCriteriaJson = JsonSerializer.Serialize(new[] { new TechnicalCriterionWeight("Specifications and compatibility", 60), new TechnicalCriterionWeight("Delivery and warranty", 40) }),
                ApprovalPolicyJson = JsonSerializer.Serialize(policy)
            };
            foreach (var member in tender.CommitteeMembers)
                weights.Proposals.Add(new WeightProposal { CommitteeMemberId = member.Id, MemberUserId = member.UserId, Financial = 40, Technical = 60, SubmittedAt = now.AddDays(-15) });
            db.Add(weights);
            db.AuditLogs.Add(new AuditLog { EntityName = nameof(Tender), EntityId = tender.Id.ToString(), UserId = officer.Id,
                Action = "PresentationFixtureCreated", NewValues = Synthetic, CreatedAt = now });
            if (bidding)
            {
                var invitation = round.Invitations.Single(i => i.SupplierId == supplier.SupplierId);
                Notify(supplier.Id, title, "Two suppliers have submitted. Your completed draft is ready to submit.", $"/Supplier/Offers/Create/{invitation.Id}");
                return (tender.Id, invitation.Id);
            }

            var session = new EvaluationSession
            {
                TenderRoundId = round.Id, WeightRound = weights, State = SessionState.Scheduled, StartsAt = now.AddHours(-2),
                EndsAt = now.AddDays(7), MeetingUrl = "https://example.invalid/presentation-meeting", FinalApprovalPolicyJson = JsonSerializer.Serialize(policy),
                AiAnalysis = new AiAnalysis { TenderRoundId = round.Id, Status = "Completed", Model = "presentation-fixture",
                    Findings = "Synthetic analysis for the presentation. All three current offers meet the specifications.",
                    EvidenceJson = "{\"syntheticDemo\":true}", StartedAt = now.AddDays(-1), ReviewedAt = now.AddDays(-1), ReviewedByUserId = officer.Id },
                CommitteeRound = new CommitteeRound { TenderRoundId = round.Id, CommitteeType = CommitteeType.Evaluation,
                    RoundNumber = 1, StartedAt = now.AddHours(-2), FinancialWeight = 40, TechnicalWeight = 60,
                    Criteria = { new EvaluationCriterion { Type = CriterionType.Financial, Name = "Financial", Weight = 40, MaxScore = 100, DisplayOrder = 1 },
                        new EvaluationCriterion { Type = CriterionType.Technical, Name = "Technical", Weight = 60, MaxScore = 100, DisplayOrder = 2 } } }
            };
            foreach (var member in tender.CommitteeMembers)
                session.Invitations.Add(new EvaluationParticipation { CommitteeMemberId = member.Id, SentAt = now.AddDays(-1) });
            var bids = round.Invitations.SelectMany(i => i.Bids).OrderBy(b => b.Items.Single().UnitPrice).ToArray();
            foreach (var bid in bids) session.EligibleBids.Add(new SessionBid { BidId = bid.Id });
            db.Add(session);
            await db.SaveChangesAsync();

            var scores = bids.Select((bid, index) => new ScoreInput { BidId = bid.Id, Financial = 95 - 8 * index,
                TechnicalScores = [94 - 6 * index, 92 - 6 * index], Justification = "Synthetic score based on the published price, specifications, delivery and warranty." }).ToList();
            var signature = Convert.FromBase64String(DemoSignature);
            foreach (var (evaluator, _) in evaluationSeats.Take(top ? 4 : 3))
                await workflow.SubmitEvaluation(tender.Id, scores, Actor(evaluator), signature);
            if (!top)
            {
                Notify(technical.Id, title, "Three evaluations are submitted. Score and sign to complete this round.", $"/EvaluationCommittee/Workflow/Meeting/{tender.Id}");
                return (tender.Id, tender.Id);
            }

            await workflow.Forward(tender.Id, Actor(officer));
            var reportId = session.ReportId!.Value;
            var managerActor = Actor(manager);
            var summary = (await workflow.ExecutiveSummary(tender.Id, managerActor)).Input;
            summary.ManagerAssessment = "Presentation only. Recommend the highest-ranked compliant offer based on the committee's fictional evaluations.";
            var pdf = await workflow.ReportPdf(reportId, managerActor);
            await workflow.SubmitExecutiveSummary(tender.Id, summary, managerActor, pdf.CanonicalHash, true, signature);
            foreach (var role in new[] { SystemRoles.ChiefExecutiveOfficer, SystemRoles.ChiefFinancialOfficer, SystemRoles.ChiefTechnologyOfficer })
            {
                var executive = Actor(await TeamUser(role));
                await workflow.ReportPdf(reportId, executive);
                await workflow.TopVote(tender.Id, VoteDecision.Approve, "", executive, pdf.CanonicalHash, true, signature);
            }
            var last = await TeamUser(SystemRoles.ChiefOperationsOfficer);
            Notify(last.Id, title, "Three Top Committee approvals are recorded. Review the summary, then sign and vote.", $"/TopCommittee/SignedReports/Review/{reportId}");
            return (tender.Id, reportId);
        }

        TenderRound MakeRound(int number, TenderRoundStatus status, DateTime starts, DateTime ends) => new()
        {
            RoundNumber = number, Status = status, OpensAt = starts, Deadline = ends, CreatedAt = starts, CreatedByUserId = officer.Id,
            ClosedAt = status == TenderRoundStatus.Open ? null : ends
        };

        TenderInvitation MakeInvitation(int supplierId, TenderItem item, int index, DateTime opens, BidStatus status, bool checkedForCompliance)
        {
            var bid = new Bid
            {
                Status = status, CurrencyCode = "JOD", CreatedAt = opens.AddHours(1), DeliveryDays = 10 + index * 3,
                SubmittedAt = status == BidStatus.Submitted ? opens.AddHours(2) : null,
                PaymentTerms = SupplierOfferService.PaymentTermOptions[1], ValidUntil = now.AddDays(90).Date,
                WarrantyMonths = 36, TechnicalProposal = "Presentation offer. Installation and delivery are included.",
                ComplianceStatus = checkedForCompliance ? ComplianceStatus.Compliant : ComplianceStatus.NotChecked,
                ComplianceCheckedAt = checkedForCompliance ? opens.AddDays(2) : null,
                ComplianceCheckedByUserId = checkedForCompliance ? officer.Id : null,
                ComplianceNotes = checkedForCompliance ? "Fictional offer meets the published requirements." : null,
                Items = { new BidItem { TenderItem = item, Quantity = item.Quantity, UnitPrice = 875 + 100 * index,
                    OfferedProduct = $"{item.Name} P{index + 1}", OfferedSpecifications = item.Specifications } }
            };
            return new TenderInvitation { SupplierId = supplierId, InvitedByUserId = officer.Id, InvitedAt = opens, RespondedAt = opens.AddHours(1),
                Status = InvitationStatus.Accepted, Bids = { bid } };
        }

        void Notify(string userId, string title, string message, string link) =>
            db.Notifications.Add(new Notification { UserId = userId, Title = title, Message = message, Link = link, CreatedAt = now });
    }

    private static ClaimsPrincipal Actor(ApplicationUser account) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id)], "PresentationSeed"));
}
