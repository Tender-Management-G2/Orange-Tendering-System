using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;
using Orange_Tendering_System.Services.Reports;

internal static class AnalyticsDemoSeed
{
    private const string Prefix = "DEMO Analytics";
    private const string ContractName = "Demo analytics contract - Network equipment.pdf";
    private const string AnchorDepartment = Prefix + " - Technology";

    public static async Task RunAsync(string connection)
    {
        if (!TeamDevDatabase.IsLocalTeamDatabase(connection))
            throw new InvalidOperationException("Analytics fixtures require a local OrangeTenderingTeamDev database.");

        var projectRoot = Path.GetFullPath("Orange-Tendering-System");
        if (!File.Exists(Path.Combine(projectRoot, "Orange-Tendering-System.csproj")))
            throw new InvalidOperationException("Run the analytics seed from the solution folder.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = projectRoot });
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connection));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<AuditLogService>();
        builder.Services.AddScoped<FinalTenderContractService>();
        builder.Services.AddScoped<OrangeReportRenderer>();
        builder.Services.AddScoped<ReportExport>();
        await using var host = builder.Build();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!(await db.Database.GetAppliedMigrationsAsync()).Any(m => m.EndsWith("_AdminAnalyticsAccessAndContracts", StringComparison.Ordinal)))
            throw new InvalidOperationException("Start the local application once to create the team database and apply the analytics migration.");

        var export = scope.ServiceProvider.GetRequiredService<ReportExport>();
        var now = DateTime.UtcNow;
        var accounts = await LoadActorsAsync(db);
        if (!await db.Departments.AnyAsync(d => d.Name == AnchorDepartment))
            await AddRecordsAsync(db, accounts, projectRoot, export, now);
        var award = await db.Tenders.AsNoTracking().SingleAsync(t => t.Title == Prefix + " - Network equipment");
        if (!await db.FinalTenderContracts.AnyAsync(c => c.TenderId == award.Id && c.FileName == ContractName))
        {
            var bytes = SamplePdf(export, "DEMO CONTRACT - Network equipment", now,
            [
                new("Tender", award.Title),
                new("Supplier", Prefix + " - Atlas Networks"),
                new("Awarded amount", "12,540.00 JOD"),
                new("Delivery", "Within 14 days"),
                new("Purpose", "Fictional training document. No signatures or obligations.")
            ]);
            using var stream = new MemoryStream(bytes);
            var file = new FormFile(stream, 0, bytes.Length, "pdf", ContractName);
            if (!await scope.ServiceProvider.GetRequiredService<FinalTenderContractService>().SaveAsync(award.Id, file, accounts.Admin, default))
                throw new InvalidOperationException("The demo award could not be found.");
        }
        var database = new SqlConnectionStringBuilder(connection).InitialCatalog;
        var requests = await db.PurchaseRequests.CountAsync(r => r.Title.StartsWith(Prefix));
        var tenders = await db.Tenders.CountAsync(t => t.Title.StartsWith(Prefix));
        var suppliers = await db.Suppliers.CountAsync(s => s.CompanyName.StartsWith(Prefix));
        var offers = await db.Bids.CountAsync(b => b.TenderInvitation.TenderRound.Tender.Title.StartsWith(Prefix));
        Console.WriteLine($"{database}: {requests} demo requests, {tenders} tenders, {suppliers} suppliers and {offers} offers.");
        Console.WriteLine("Search for DEMO Analytics under Reports & Analytics to isolate the examples.");
    }

    private sealed record Actors(string Admin, string Requester, string Officer, string Manager,
        string Department, string Finance, string Technical, string[] Executives);

    private static async Task<Actors> LoadActorsAsync(ApplicationDbContext db)
    {
        var emails = TeamAccountSeeder.Accounts.Select(a => a.Email).ToArray();
        var accounts = await db.Users.AsNoTracking().Where(u => u.Email != null && emails.Contains(u.Email) && u.IsActive)
            .Select(u => new { u.Email, u.Id })
            .ToDictionaryAsync(u => u.Email!, u => u.Id);
        string For(string role) => accounts.TryGetValue(TeamAccountSeeder.Find(role).Email, out var id) ? id :
            throw new InvalidOperationException($"Missing active team account for {role}. Start the local application and allow team accounts to initialize.");
        return new(For(SystemRoles.Admin), For(SystemRoles.RequestingDepartment), For(SystemRoles.SourcingOfficer),
            For(SystemRoles.SourcingManager), For(SystemRoles.DepartmentRepresentative), For(SystemRoles.Finance), For(SystemRoles.Technical),
            [For(SystemRoles.ChiefExecutiveOfficer), For(SystemRoles.ChiefFinancialOfficer), For(SystemRoles.ChiefTechnologyOfficer), For(SystemRoles.ChiefOperationsOfficer)]);
    }

    private static async Task AddRecordsAsync(ApplicationDbContext db, Actors people, string projectRoot, ReportExport export, DateTime now)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (await db.Departments.AnyAsync(d => d.Name == AnchorDepartment)) return;

        var departments = new[] { AnchorDepartment, Prefix + " - Facilities", Prefix + " - Operations" }
            .Select(name => new Department { Name = name, Description = "Fictional department for the reporting demo." }).ToArray();
        db.Departments.AddRange(departments);
        var suppliers = new[]
        {
            Supplier("Atlas Networks", "01", SupplierStatus.Approved, SupplierCategories.InformationTechnology, 55),
            Supplier("Cedar Office", "02", SupplierStatus.Approved, SupplierCategories.OfficeSupplies, 42),
            Supplier("Petra Facilities", "03", SupplierStatus.Suspended, SupplierCategories.Facilities, 28),
            Supplier("Northstar Services", "04", SupplierStatus.Pending, SupplierCategories.ProfessionalServices, 12),
            Supplier("Summit Trading", "05", SupplierStatus.Rejected, SupplierCategories.Other, 19)
        };
        db.Suppliers.AddRange(suppliers);
        Supplier Supplier(string name, string code, SupplierStatus status, string category, int days) => new()
        {
            CompanyName = $"{Prefix} - {name}", RegistrationNumber = "REPORT-DEMO-" + code,
            Email = $"report-demo-vendor-{code}@example.invalid", Category = category, Status = status, CreatedAt = now.AddDays(-days),
            ReviewedByUserId = status == SupplierStatus.Pending ? null : people.Admin,
            ReviewedAt = status == SupplierStatus.Pending ? null : now.AddDays(-days + 2)
        };

        var requests = new[]
        {
            Request("Network equipment request", 0, PurchaseRequestStatus.ConvertedToTender, 31, "Network switch", 6),
            Request("Backup storage request", 0, PurchaseRequestStatus.ConvertedToTender, 24, "Storage appliance", 2),
            Request("Office seating request", 1, PurchaseRequestStatus.ConvertedToTender, 12, "Ergonomic chair", 24),
            Request("Air conditioning service request", 1, PurchaseRequestStatus.ConvertedToTender, 19, "Maintenance visit", 4),
            Request("Safety training request", 2, PurchaseRequestStatus.ConvertedToTender, 23, "Training session", 3),
            Request("Site access readers request", 2, PurchaseRequestStatus.ConvertedToTender, 15, "Badge reader", 10),
            Request("Spare monitor request", 0, PurchaseRequestStatus.Draft, 2, "Monitor", 8),
            Request("Labels and stationery request", 2, PurchaseRequestStatus.Submitted, 3, "Label roll", 20),
            Request("Door inspection request", 1, PurchaseRequestStatus.UnderReview, 4, "Inspection", 2),
            Request("Server cabinet request", 0, PurchaseRequestStatus.Returned, 5, "Server cabinet", 1),
            Request("Replacement signage request", 1, PurchaseRequestStatus.Rejected, 8, "Sign", 5)
        };
        db.PurchaseRequests.AddRange(requests);
        PurchaseRequest Request(string title, int department, PurchaseRequestStatus status, int days, string item, decimal quantity)
        {
            var created = now.AddDays(-days);
            var request = new PurchaseRequest
            {
                Title = $"{Prefix} - {title}", Department = departments[department], CreatedByUserId = people.Requester,
                Status = status, CreatedAt = created, SubmittedAt = status == PurchaseRequestStatus.Draft ? null : created.AddHours(2),
                Justification = "Fictional data for exploring reports.", NeededBy = now.AddDays(30), Priority = RequestPriority.Normal,
                Items = { new PurchaseRequestItem { Name = item, Quantity = quantity, Unit = "Each", Specifications = "Demo specification" } }
            };
            if (request.SubmittedAt is { } submitted)
            {
                var decision = status switch
                {
                    PurchaseRequestStatus.ConvertedToTender => ReviewDecision.Accepted,
                    PurchaseRequestStatus.Returned => ReviewDecision.Returned,
                    PurchaseRequestStatus.Rejected => ReviewDecision.Rejected,
                    _ => ReviewDecision.Pending
                };
                request.Reviews.Add(new PurchaseRequestReview
                {
                    SubmissionNumber = 1, SubmittedAt = submitted, Decision = decision,
                    ReviewedAt = decision == ReviewDecision.Pending ? null : submitted.AddHours(6 + days % 6 * 4),
                    ReviewedByUserId = decision == ReviewDecision.Pending ? null : people.Officer,
                    Comment = decision switch
                    {
                        ReviewDecision.Returned => "Confirm cabinet dimensions and power needs.",
                        ReviewDecision.Rejected => "Duplicate request for an existing order.",
                        _ => null
                    }
                });
            }
            return request;
        }

        var network = Tender(0, "Network equipment", TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, 27, 17, 13000m);
        var storage = Tender(1, "Backup storage", TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, 20, 12, 18800m);
        var seating = Tender(2, "Office seating", TenderStatus.Bidding, TenderRoundStatus.Open, 6, -14, 7800m);
        var cooling = Tender(3, "Air conditioning service", TenderStatus.Evaluation, TenderRoundStatus.SentToEvaluation, 14, 5, 7200m);
        var training = Tender(4, "Safety training - no offers", TenderStatus.Cancelled, TenderRoundStatus.Failed, 18, 9, 4300m);
        var access = Tender(5, "Site access readers", TenderStatus.Rejected, TenderRoundStatus.SentToEvaluation, 11, 4, 8700m);
        Tender Tender(int requestIndex, string title, TenderStatus status, TenderRoundStatus roundStatus, int days, int closes, decimal estimated)
        {
            var request = requests[requestIndex];
            var item = request.Items.Single();
            var tender = new Tender
            {
                PurchaseRequest = request, Title = $"{Prefix} - {title}", Description = "Fictional tender for reports and analytics.",
                Status = status, CreatedByUserId = people.Officer, CreatedAt = now.AddDays(-days),
                EstimatedValue = estimated, ClosedAt = closes > 0 ? now.AddDays(-closes) : null,
                Items = { new TenderItem { Name = item.Name, Quantity = item.Quantity, Unit = item.Unit, Specifications = item.Specifications } },
                Rounds =
                {
                    new TenderRound { RoundNumber = 1, Status = roundStatus, CreatedByUserId = people.Officer, CreatedAt = now.AddDays(-days),
                        OpensAt = now.AddDays(-days + 1), Deadline = now.AddDays(-closes), ClosedAt = closes > 0 ? now.AddDays(-closes) : null }
                }
            };
            db.Tenders.Add(tender);
            return tender;
        }

        var networkBid = Offer(network, 0, 2050m, 240m);
        var networkAlternate = Offer(network, 1, 2240m, 170m);
        Offer(network, 2, 1940m, 100m, excluded: true);
        var storageBid = Offer(storage, 0, 8700m, 180m);
        var storageAlternate = Offer(storage, 1, 9000m, 120m);
        Offer(seating, 1, 265m, 80m);
        Invite(seating, 0, false);
        Invite(seating, 2, false);
        Offer(cooling, 2, 1560m, 100m);
        Offer(cooling, 1, 1650m, 100m);
        Offer(cooling, 0, 1720m, 100m);
        Invite(training, 0, false);
        Invite(training, 1, false);
        var accessBid = Offer(access, 0, 795m, 150m);
        var accessAlternate = Offer(access, 2, 805m, 140m);
        TenderInvitation Invite(Tender tender, int supplierIndex, bool submitted)
        {
            var round = tender.Rounds.Single();
            var invited = round.OpensAt!.Value;
            var invitation = new TenderInvitation
            {
                TenderRound = round, Supplier = suppliers[supplierIndex], InvitedByUserId = people.Officer, InvitedAt = invited,
                Status = submitted ? InvitationStatus.Accepted : InvitationStatus.Pending, RespondedAt = submitted ? invited.AddDays(1) : null
            };
            db.TenderInvitations.Add(invitation);
            return invitation;
        }
        Bid Offer(Tender tender, int supplierIndex, decimal unitPrice, decimal shipping, bool excluded = false)
        {
            var item = tender.Items.Single();
            var invitation = Invite(tender, supplierIndex, true);
            var bid = new Bid
            {
                TenderInvitation = invitation, Status = BidStatus.Submitted, SubmittedAt = invitation.RespondedAt,
                CreatedAt = invitation.InvitedAt, CurrencyCode = "JOD", ShippingCost = shipping, DeliveryDays = 14 + supplierIndex * 3,
                TechnicalProposal = $"{item.Name} with installation and a two-year support period.",
                ComplianceStatus = excluded ? ComplianceStatus.NonCompliant : ComplianceStatus.Compliant,
                ComplianceNotes = excluded ? "Mandatory warranty certificate was missing." : "Required specifications and attachments verified.",
                MissingRequirements = excluded ? "Manufacturer warranty certificate" : null,
                Items = { new BidItem { TenderItem = item, Quantity = item.Quantity, UnitPrice = unitPrice, OfferedSpecifications = item.Specifications } }
            };
            db.Bids.Add(bid);
            return bid;
        }

        await db.SaveChangesAsync();
        var finalNotices = new List<(FinalDecisionNotice Notice, TenderReport Report)>();
        AddEvaluation(network, networkBid, networkAlternate, 91, 84, now.AddDays(-9), ReportStatus.Accepted);
        AddEvaluation(storage, storageBid, storageAlternate, 89, 82, now.AddDays(-6), ReportStatus.Accepted);
        AddEvaluation(access, accessBid, accessAlternate, 82, 77, now.AddDays(-2), ReportStatus.Rejected);
        network.AwardedBid = networkBid;
        network.AwardedAt = now.AddDays(-7);
        network.AwardedByUserId = people.Executives[0];
        storage.AwardedBid = storageBid;
        storage.AwardedAt = now.AddDays(-4);
        storage.AwardedByUserId = people.Executives[0];

        void AddEvaluation(Tender tender, Bid preferred, Bid alternative, int score, int alternateScore, DateTime submitted, ReportStatus status)
        {
            var round = tender.Rounds.Single();
            var ranking = WeightedCalculation.Rank(new[] { (preferred.Id, (decimal)score), (alternative.Id, (decimal)alternateScore) });
            var report = new TenderReport
            {
                TenderRound = round, ReportType = ReportType.FullEvaluation, RecommendedBidId = preferred.Id,
                CreatedByUserId = people.Officer, CreatedAt = submitted.AddHours(-2), SubmittedAt = submitted,
                ReviewedByUserId = people.Manager, ReviewedAt = submitted.AddDays(1), Status = status,
                ReviewComment = status == ReportStatus.Rejected ? "Delivery commitment did not meet the final project schedule." : "Verified the evaluation evidence and recommendation.",
                Content = JsonSerializer.Serialize(new { Justification = "Fictional result based on weighted technical and financial scores." }),
                RankingSnapshotJson = JsonSerializer.Serialize(ranking)
            };
            var committee = EvaluationCommittee(tender, submitted);
            var scoreRound = new CommitteeRound
            {
                TenderRound = round, CommitteeType = CommitteeType.Evaluation, RoundNumber = 1, Status = CommitteeRoundStatus.Approved,
                TechnicalWeight = 60, FinancialWeight = 40, StartedAt = submitted.AddDays(-2), CompletedAt = submitted, TenderReport = report
            };
            var technical = new EvaluationCriterion { CommitteeRound = scoreRound, Type = CriterionType.Technical, Name = "Technical fit and support", Weight = 60, MaxScore = 100 };
            var financial = new EvaluationCriterion { CommitteeRound = scoreRound, Type = CriterionType.Financial, Name = "Total proposed cost", Weight = 40, MaxScore = 100 };
            foreach (var member in committee)
            {
                AddScores(preferred, member, score);
                AddScores(alternative, member, alternateScore);
            }
            void AddScores(Bid bid, CommitteeMember member, int result)
            {
                db.EvaluationScores.AddRange(
                    new EvaluationScore { Bid = bid, CommitteeMember = member, EvaluationCriterion = technical, Score = result + 2, ScoredAt = submitted.AddHours(-1) },
                    new EvaluationScore { Bid = bid, CommitteeMember = member, EvaluationCriterion = financial, Score = result - 3, ScoredAt = submitted.AddHours(-1) });
            }
            if (status != ReportStatus.Accepted) return;
            var votingRound = new CommitteeRound
            {
                TenderRound = round, TenderReport = report, CommitteeType = CommitteeType.Top, RoundNumber = 1,
                Status = CommitteeRoundStatus.Approved, StartedAt = submitted.AddDays(1), CompletedAt = submitted.AddDays(2)
            };
            for (var i = 0; i < people.Executives.Length; i++)
            {
                var seat = new CommitteeMember { Tender = tender, UserId = people.Executives[i], CommitteeType = CommitteeType.Top,
                    ExecutivePosition = (TopExecutivePosition)i, AssignedAt = submitted.AddDays(1) };
                db.CommitteeVotes.Add(new CommitteeVote { CommitteeMember = seat, CommitteeRound = votingRound,
                    Decision = VoteDecision.Approve, Comment = "Demo approval.", VotedAt = submitted.AddDays(2) });
            }
            var notice = new FinalDecisionNotice
            {
                Tender = tender, Outcome = FinalDecisionOutcome.Award,
                Status = FinalDecisionStatus.SuppliersNotified, TopApprovals = 4, TopVotes = 4,
                CreatedAt = submitted.AddDays(2), ManagerAcknowledgedAt = submitted.AddDays(2), SuppliersNotifiedAt = submitted.AddDays(2)
            };
            db.FinalDecisionNotices.Add(notice);
            finalNotices.Add((notice, report));
        }

        CommitteeMember[] EvaluationCommittee(Tender tender, DateTime assigned) =>
            new (string User, CommitteeResponsibility Seat)[]
            {
                (people.Manager, CommitteeResponsibility.SourcingManager), (people.Department, CommitteeResponsibility.DepartmentRepresentative),
                (people.Finance, CommitteeResponsibility.FinancialRepresentative), (people.Technical, CommitteeResponsibility.TechnicalRepresentative)
            }.Select(member => new CommitteeMember { Tender = tender, UserId = member.User, CommitteeType = CommitteeType.Evaluation,
                Responsibility = member.Seat, AssignedAt = assigned.AddDays(-2) }).ToArray();

        var coolingRound = cooling.Rounds.Single();
        var coolingCommittee = EvaluationCommittee(cooling, now.AddDays(-4));
        var late = new EvaluationSession
        {
            TenderRound = coolingRound,
            WeightRound = new WeightRound { Tender = cooling, Version = 1, State = WeightState.Approved, ApprovedTechnical = 60,
                ApprovedFinancial = 40, ApprovedAt = now.AddDays(-5), ApprovedByUserId = people.Manager, CreatedAt = now.AddDays(-6) },
            AiAnalysis = new AiAnalysis { TenderRound = coolingRound, StartedAt = now.AddDays(-3), Status = "Completed", EvidenceJson = "[]" },
            CommitteeRound = new CommitteeRound { TenderRound = coolingRound, CommitteeType = CommitteeType.Evaluation,
                RoundNumber = 1, StartedAt = now.AddDays(-3), TechnicalWeight = 60, FinancialWeight = 40 },
            StartsAt = now.AddDays(-3), EndsAt = now.AddDays(-1), ClosedAt = now.AddDays(-1),
            State = SessionState.Incomplete, ClosureReason = "Scheduled deadline reached"
        };
        db.Add(late);
        for (var index = 0; index < coolingCommittee.Length; index++)
            db.Add(new EvaluationParticipation
            {
                CommitteeMember = coolingCommittee[index], EvaluationSession = late,
                SentAt = now.AddDays(-4), SubmittedAt = index < 2 ? now.AddDays(-2) : null
            });

        foreach (var tender in new[] { network, storage, seating, cooling, training, access })
        {
            Audit(tender, people.Officer, "DemoTenderCreated", tender.CreatedAt);
            Audit(tender, people.Manager, "DemoTenderStatusRecorded", tender.ClosedAt ?? now);
        }
        Audit(network, people.Executives[0], "DemoAwardRecorded", network.AwardedAt!.Value);
        Audit(storage, people.Executives[0], "DemoAwardRecorded", storage.AwardedAt!.Value);
        void Audit(Tender tender, string user, string action, DateTime date) =>
            db.AuditLogs.Add(new AuditLog { UserId = user, EntityName = nameof(Tender), EntityId = tender.Id.ToString(),
                Action = action, CreatedAt = date, NewValues = JsonSerializer.Serialize(new { Demo = true, tender.Title, Status = tender.Status.ToString() }) });

        await AddSupplierDocument(suppliers[0], "atlas-registration", "CommercialRegistration", DocumentVerificationStatus.Verified, 24);
        await AddSupplierDocument(suppliers[1], "cedar-tax", "TaxCertificate", DocumentVerificationStatus.Verified, 110);
        await AddSupplierDocument(suppliers[2], "petra-registration", "CommercialRegistration", DocumentVerificationStatus.Verified, -7);
        await AddSupplierDocument(suppliers[3], "northstar-registration", "CommercialRegistration", DocumentVerificationStatus.Pending, 36);
        await db.SaveChangesAsync();
        foreach (var (notice, report) in finalNotices) notice.TenderReportId = report.Id;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        async Task AddSupplierDocument(Supplier supplier, string key, string type, DocumentVerificationStatus status, int expiresIn)
        {
            var name = $"analytics-demo-{key}.pdf";
            var folder = Path.Combine(projectRoot, "App_Data", "SupplierDocuments");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            if (!File.Exists(path))
                await File.WriteAllBytesAsync(path, SamplePdf(export, $"DEMO DOCUMENT - {type}", now,
                    [new("Supplier", supplier.CompanyName), new("Purpose", "Fictional document for the reporting demo.")]));
            db.Documents.Add(new Document { Supplier = supplier, FileName = name, ContentType = "application/pdf",
                StoragePath = $"App_Data/SupplierDocuments/{name}", SizeBytes = new FileInfo(path).Length, DocumentType = type,
                UploadedAt = supplier.CreatedAt, UploadedByUserId = people.Admin, ExpiresAt = now.AddDays(expiresIn),
                VerificationStatus = status, VerifiedAt = status == DocumentVerificationStatus.Verified ? supplier.ReviewedAt : null,
                VerifiedByUserId = status == DocumentVerificationStatus.Verified ? people.Admin : null });
        }
    }

    private static byte[] SamplePdf(ReportExport export, string title, DateTime now, IEnumerable<ReportKpi> facts)
    {
        var model = new ReportModel { Title = title, GeneratedAt = now };
        model.Kpis.AddRange(facts);
        return export.ToPdf(model);
    }
}
