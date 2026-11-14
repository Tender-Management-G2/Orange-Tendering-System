using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

/// <summary>Development-only data for walking the sourcing workflow. Never invoked in Production.</summary>
public static class DemoDataSeeder
{
    public const string Password = "OrangeDemo2026!";

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await db.Departments.AnyAsync(d => d.Name == "DEMO - Information Technology"))
        {
            // Add newly introduced demo pieces to an existing sample database as well.
            var demoRounds = await db.TenderRounds.Include(r => r.Tender)
                .Where(r => r.Tender.Title.StartsWith("DEMO") && r.Status == TenderRoundStatus.Open && r.Tender.Status == TenderStatus.Bidding)
                .ToListAsync();
            foreach (var demoRound in demoRounds)
            {
                // Keep sample bidding flows open during local testing; production rounds are never modified here.
                demoRound.OpensAt = DateTime.UtcNow.AddDays(-1);
                demoRound.Deadline = DateTime.UtcNow.AddDays(7);
            }
            await db.SaveChangesAsync();

            var existingOfficer = await users.FindByEmailAsync("officer@orange-demo.local");
            var existingSupplier = await users.FindByEmailAsync("supplier3@orange-demo.local");
            var existingBid = await db.Bids.Include(b => b.TenderInvitation).ThenInclude(i => i.TenderRound).ThenInclude(r => r.Tender)
                .SingleOrDefaultAsync(b => b.TenderInvitation.TenderRound.Tender.Title == "DEMO Tender - Laptops ready for committee" && b.ComplianceStatus == ComplianceStatus.NonCompliant);
            var demoCategories = new[] { SupplierCategories.InformationTechnology, SupplierCategories.OfficeSupplies, SupplierCategories.Furniture, SupplierCategories.ProfessionalServices };
            for (var i = 1; i <= demoCategories.Length; i++)
            {
                var demoSupplier = await db.Suppliers.SingleOrDefaultAsync(s => s.CompanyName == $"DEMO Supplier {i}" && s.Category == SupplierCategories.Other);
                if (demoSupplier != null) demoSupplier.Category = demoCategories[i - 1];
            }
            await db.SaveChangesAsync();
            if (existingOfficer != null && existingSupplier != null && existingBid != null && !await db.Clarifications.AnyAsync(c => c.BidId == existingBid.Id && c.Subject == "DEMO - Warranty document question"))
            {
                db.Clarifications.Add(new Clarification { BidId = existingBid.Id, FromUserId = existingSupplier.Id, ToUserId = existingOfficer.Id, Subject = "DEMO - Warranty document question", Status = ClarificationStatus.Open, CreatedAt = DateTime.UtcNow.AddHours(-2), Messages = { new ClarificationMessage { SenderUserId = existingSupplier.Id, Message = "I uploaded the warranty document. Could you confirm it is the required form?", SentAt = DateTime.UtcNow.AddHours(-2) } } });
                db.Notifications.Add(new Notification { UserId = existingOfficer.Id, Title = "Supplier clarification", Message = "A supplier sent a message about a submitted offer.", Link = "/SourcingOfficer/Clarifications", CreatedAt = DateTime.UtcNow.AddHours(-2) });
                await db.SaveChangesAsync();
            }
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var department = new Department { Name = "DEMO - Information Technology", Description = "Development sample data", IsActive = true };
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        var officer = await CreateUser("officer@orange-demo.local", "Demo Sourcing Officer", SystemRoles.SourcingOfficer, department.Id);
        var requester = await CreateUser("requester@orange-demo.local", "Demo Requester", SystemRoles.RequestingDepartment, department.Id);
        var manager = await CreateUser("manager@orange-demo.local", "Demo Sourcing Manager", SystemRoles.SourcingManager, null);
        var committeeUsers = new List<ApplicationUser>();
        for (var i = 1; i <= 4; i++) committeeUsers.Add(await CreateUser($"committee{i}@orange-demo.local", $"Demo Evaluator {i}", SystemRoles.EvaluationCommittee, null));

        var supplierUsers = new List<(Supplier Supplier, ApplicationUser User)>();
        for (var i = 1; i <= 4; i++)
        {
            var categories = new[] { SupplierCategories.InformationTechnology, SupplierCategories.OfficeSupplies, SupplierCategories.Furniture, SupplierCategories.ProfessionalServices };
            var supplier = new Supplier { CompanyName = $"DEMO Supplier {i}", Category = categories[i - 1], RegistrationNumber = $"DEMO-REG-{i:000}", TaxNumber = $"DEMO-TAX-{i:000}", Email = $"supplier{i}@orange-demo.local", Phone = $"+9627900000{i:00}", Address = "Amman, Jordan", Status = SupplierStatus.Approved, CreatedAt = now };
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            var account = await CreateUser($"supplier{i}@orange-demo.local", $"Demo Supplier User {i}", SystemRoles.Supplier, null, supplier.Id);
            supplierUsers.Add((supplier, account));
        }

        PurchaseRequest NewRequest(string title, PurchaseRequestStatus status) => new()
        {
            DepartmentId = department.Id, CreatedByUserId = requester.Id, Title = title,
            Justification = "Development sample: replace laptops used by the IT support team.", Priority = RequestPriority.High,
            NeededBy = now.AddMonths(2), Status = status, CreatedAt = now.AddDays(-8), SubmittedAt = now.AddDays(-7),
            Items = { new PurchaseRequestItem { Name = "Business laptop", Specifications = "16 GB RAM, 512 GB SSD", Quantity = 10, Unit = "unit" } }
        };
        var readyRequest = NewRequest("DEMO - Laptops ready for tender", PurchaseRequestStatus.Accepted);
        var submittedRequest = NewRequest("DEMO - Monitors awaiting review", PurchaseRequestStatus.Submitted);
        var returnedRequest = NewRequest("DEMO - Network switches returned", PurchaseRequestStatus.Returned);
        var rejectedRequest = NewRequest("DEMO - Old printer request rejected", PurchaseRequestStatus.Rejected);
        db.PurchaseRequests.AddRange(readyRequest, submittedRequest, returnedRequest, rejectedRequest);
        await db.SaveChangesAsync();
        foreach (var req in new[] { returnedRequest, rejectedRequest })
            db.PurchaseRequestReviews.Add(new PurchaseRequestReview { PurchaseRequestId = req.Id, SubmissionNumber = 1, SubmittedAt = req.SubmittedAt!.Value, SubmittedSnapshotJson = "{\"demo\":true}", Decision = req.Status == PurchaseRequestStatus.Returned ? ReviewDecision.Returned : ReviewDecision.Rejected, Comment = req.Status == PurchaseRequestStatus.Returned ? "Please attach the technical specifications." : "The request is outside the approved budget." , ReviewedByUserId = officer.Id, ReviewedAt = now.AddDays(-3) });

        var activeTender = CreateTender(readyRequest, officer.Id, now, TenderStatus.Bidding, TenderRoundStatus.Open);
        db.Tenders.Add(activeTender);
        var evaluationRequest = NewRequest("DEMO - Office equipment under evaluation", PurchaseRequestStatus.Accepted);
        var returnedReportRequest = NewRequest("DEMO - Security cameras report returned", PurchaseRequestStatus.Accepted);
        var rejectedReportRequest = NewRequest("DEMO - Storage servers report rejected", PurchaseRequestStatus.Accepted);
        var acceptedReportRequest = NewRequest("DEMO - Wi-Fi equipment report approved", PurchaseRequestStatus.Accepted);
        var committeeReadyRequest = NewRequest("DEMO - Laptops ready for committee", PurchaseRequestStatus.Accepted);
        db.PurchaseRequests.AddRange(evaluationRequest, returnedReportRequest, rejectedReportRequest, acceptedReportRequest, committeeReadyRequest);
        await db.SaveChangesAsync();

        var evalTender = CreateTender(evaluationRequest, officer.Id, now.AddDays(-20), TenderStatus.ManagerReview, TenderRoundStatus.SentToEvaluation);
        var returnedTender = CreateTender(returnedReportRequest, officer.Id, now.AddDays(-30), TenderStatus.OfficerReview, TenderRoundStatus.SentToEvaluation);
        var rejectedTender = CreateTender(rejectedReportRequest, officer.Id, now.AddDays(-40), TenderStatus.Rejected, TenderRoundStatus.SentToEvaluation);
        var acceptedTender = CreateTender(acceptedReportRequest, officer.Id, now.AddDays(-50), TenderStatus.TopCommitteeReview, TenderRoundStatus.SentToEvaluation);
        var committeeReadyTender = CreateTender(committeeReadyRequest, officer.Id, now.AddDays(-1), TenderStatus.Bidding, TenderRoundStatus.Open);
        db.Tenders.AddRange(evalTender, returnedTender, rejectedTender, acceptedTender, committeeReadyTender);
        await db.SaveChangesAsync();

        // Four invitations, with pending and declined states for the officer to inspect.
        for (var i = 0; i < supplierUsers.Count; i++)
        {
            var status = i switch { 0 => InvitationStatus.Accepted, 1 => InvitationStatus.Accepted, 2 => InvitationStatus.Pending, _ => InvitationStatus.Declined };
            activeTender.Rounds.Single().Invitations.Add(new TenderInvitation { SupplierId = supplierUsers[i].Supplier.Id, InvitedByUserId = officer.Id, InvitedAt = now.AddDays(-2), Status = status, RespondedAt = status == InvitationStatus.Accepted ? now.AddDays(-1) : null });
        }
        await db.SaveChangesAsync();

        AddCommittee(evalTender, committeeUsers, now);
        AddCommittee(returnedTender, committeeUsers, now);
        AddCommittee(rejectedTender, committeeUsers, now);
        AddCommittee(acceptedTender, committeeUsers, now);
        AddCommittee(committeeReadyTender, committeeUsers, now);
        await db.SaveChangesAsync();
        AddEvaluationReport(evalTender, committeeUsers[0].Id, manager.Id, now, ReportStatus.Submitted, "Committee report submitted; ready for officer review.");
        AddEvaluationReport(returnedTender, committeeUsers[0].Id, manager.Id, now, ReportStatus.Returned, "Please clarify the delivery schedule before resubmission.");
        AddEvaluationReport(rejectedTender, committeeUsers[0].Id, manager.Id, now, ReportStatus.Rejected, "The committee rejected this recommendation.");
        AddEvaluationReport(acceptedTender, committeeUsers[0].Id, manager.Id, now, ReportStatus.Accepted, "Approved; may proceed to the Top Committee.");
        await db.SaveChangesAsync();

        // Three compliant offers plus one non-compliant offer let the Officer exercise committee forwarding.
        var committeeRound = committeeReadyTender.Rounds.Single();
        foreach (var (supplier, _) in supplierUsers)
        {
            var compliant = supplier.Id != supplierUsers[2].Supplier.Id;
            var invitation = new TenderInvitation { TenderRoundId = committeeRound.Id, SupplierId = supplier.Id, InvitedByUserId = officer.Id, InvitedAt = now.AddDays(-3), RespondedAt = now.AddDays(-2), Status = InvitationStatus.Accepted };
            var bid = new Bid { TenderInvitation = invitation, RevisionNumber = 1, Status = BidStatus.Submitted, CurrencyCode = "JOD", DeliveryDays = 14 + supplier.Id % 5, TechnicalProposal = "Meets the requested specifications. Demo offer for workflow review.", SubmittedAt = now.AddDays(-1), CreatedAt = now.AddDays(-2), ComplianceStatus = compliant ? ComplianceStatus.Compliant : ComplianceStatus.NonCompliant, ComplianceNotes = compliant ? "Mandatory documents checked." : "Missing warranty evidence.", MissingRequirements = compliant ? null : "Warranty evidence" };
            bid.Items.Add(new BidItem { TenderItem = committeeReadyTender.Items.Single(), Quantity = 10, UnitPrice = 525 + (supplier.Id % 3) * 35, OfferedSpecifications = "16 GB RAM, 512 GB SSD" });
            db.Bids.Add(bid);
        }
        await db.SaveChangesAsync();
        var clarificationBid = await db.Bids.SingleAsync(b => b.TenderInvitation.TenderRoundId == committeeRound.Id && b.ComplianceStatus == ComplianceStatus.NonCompliant);
        var clarificationThread = new Clarification
        {
            BidId = clarificationBid.Id, FromUserId = supplierUsers[2].User.Id, ToUserId = officer.Id,
            Subject = "DEMO - Warranty document question", Status = ClarificationStatus.Open, CreatedAt = now.AddHours(-2),
            Messages = { new ClarificationMessage { SenderUserId = supplierUsers[2].User.Id, Message = "I uploaded the warranty document. Could you confirm it is the required form?", SentAt = now.AddHours(-2) } }
        };
        db.Clarifications.Add(clarificationThread);
        db.Notifications.Add(new Notification { UserId = officer.Id, Title = "Supplier clarification", Message = "A supplier sent a message about a submitted offer.", Link = "/SourcingOfficer/Clarifications", CreatedAt = now.AddHours(-2) });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        async Task<ApplicationUser> CreateUser(string email, string name, string role, int? departmentId, int? supplierId = null)
        {
            var user = await users.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, FullName = name, DepartmentId = departmentId, SupplierId = supplierId, IsActive = true, MustChangePassword = false };
                var result = await users.CreateAsync(user, Password);
                if (!result.Succeeded) throw new InvalidOperationException($"Demo account creation failed for {email}: {string.Join(", ", result.Errors.Select(x => x.Code))}");
            }
            if (!await users.IsInRoleAsync(user, role))
            {
                var roleResult = await users.AddToRoleAsync(user, role);
                if (!roleResult.Succeeded) throw new InvalidOperationException($"Could not assign demo role {role} to {email}.");
            }
            return user;
        }
    }

    private static Tender CreateTender(PurchaseRequest request, string officerId, DateTime now, TenderStatus tenderStatus, TenderRoundStatus roundStatus)
    {
        var tender = new Tender { PurchaseRequestId = request.Id, Title = request.Title.Replace("DEMO - ", "DEMO Tender - "), Description = request.Justification, Requirements = "Commercial registration; tax certificate; warranty terms", CreatedByUserId = officerId, CreatedAt = now, Status = tenderStatus };
        foreach (var item in request.Items) tender.Items.Add(new TenderItem { Name = item.Name, Specifications = item.Specifications, Quantity = item.Quantity, Unit = item.Unit });
        tender.Rounds.Add(new TenderRound { RoundNumber = 1, Status = roundStatus, OpensAt = now.AddDays(-5), Deadline = now.AddDays(3), CreatedByUserId = officerId, CreatedAt = now });
        return tender;
    }

    private static void AddCommittee(Tender tender, IReadOnlyList<ApplicationUser> users, DateTime now)
    {
        var seats = new[] { CommitteeResponsibility.SourcingManager, CommitteeResponsibility.DepartmentRepresentative, CommitteeResponsibility.FinancialRepresentative, CommitteeResponsibility.TechnicalRepresentative };
        for (var i = 0; i < seats.Length; i++) tender.CommitteeMembers.Add(new CommitteeMember { UserId = users[i].Id, CommitteeType = CommitteeType.Evaluation, Responsibility = seats[i], AssignedAt = now.AddDays(-10) });
    }

    private static void AddEvaluationReport(Tender tender, string creatorId, string managerId, DateTime now, ReportStatus status, string comment)
    {
        tender.Rounds.Single().Reports.Add(new TenderReport { ReportType = ReportType.FullEvaluation, VersionNumber = 1, Content = "DEMO report: technical and financial evaluation summary, observations, and recommendation.", RankingSnapshotJson = "[{\"rank\":1,\"supplier\":\"DEMO Supplier 1\",\"eligible\":true}]", Status = status, CreatedByUserId = creatorId, CreatedAt = now.AddDays(-2), SubmittedAt = now.AddDays(-2), ReviewedByUserId = status is ReportStatus.Accepted or ReportStatus.Returned or ReportStatus.Rejected ? managerId : null, ReviewedAt = status is ReportStatus.Accepted or ReportStatus.Returned or ReportStatus.Rejected ? now.AddDays(-1) : null, ReviewComment = status is ReportStatus.Accepted or ReportStatus.Returned or ReportStatus.Rejected ? comment : null });
    }
}
