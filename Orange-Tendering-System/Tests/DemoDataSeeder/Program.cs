using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

var connection = Environment.GetEnvironmentVariable("DEMO_SEED_CONNECTION")
    ?? throw new InvalidOperationException("Set DEMO_SEED_CONNECTION to the intended local database.");
var password = Environment.GetEnvironmentVariable("DEMO_MANAGER_PASSWORD")
    ?? throw new InvalidOperationException("Set DEMO_MANAGER_PASSWORD for the demo accounts.");

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(connection)
    .Options;
await using var db = new ApplicationDbContext(options);

const string managerEmail = "demo.manager@orange.local";
const string officerEmail = "demo.officer@orange.local";
const string departmentName = "Training Procurement Department";
const string tenderTitle = "Training: Office Furniture Purchase";

await EnsureRole(SystemRoles.SourcingManager);
await EnsureRole(SystemRoles.SourcingOfficer);
await EnsureRole(SystemRoles.TopCommittee);

if (string.Equals(Environment.GetEnvironmentVariable("RESET_DEMO_DATA"), "true", StringComparison.OrdinalIgnoreCase))
{
    await ResetDemoData();
    Console.WriteLine("Previous demo data was removed.");
}

var manager = await EnsureUser(managerEmail, "Demo Sourcing Manager", password, SystemRoles.SourcingManager);
var officer = await EnsureUser(officerEmail, "Demo Sourcing Officer", password, SystemRoles.SourcingOfficer);
var topCommittee = new List<ApplicationUser>();
for (var number = 1; number <= 4; number++)
    topCommittee.Add(await EnsureUser($"demo.top{number}@orange.local", $"Demo Top Committee {number}", password, SystemRoles.TopCommittee));

var report = await db.TenderReports
    .SingleOrDefaultAsync(item => item.Content == "Training scenario: office furniture evaluation ready for Sourcing Manager review.");

if (report == null)
    report = await CreateTrainingScenario(officer, topCommittee);

await EnsureManagerNotification(manager.Id, report.Id);

Console.WriteLine("Clear Sourcing Manager training data is ready.");
Console.WriteLine("Manager login: " + managerEmail);
Console.WriteLine("Password: the value used for DEMO_MANAGER_PASSWORD");
Console.WriteLine($"Open Pending Reports, then review report #{report.Id}: {tenderTitle}.");

async Task ResetDemoData()
{
    // Only synthetic rows created by this utility are selected. Existing project data is not touched.
    var tenderIds = await db.Tenders
        .Where(t => t.Title.StartsWith("Demo ") || t.Title.StartsWith("Training: "))
        .Select(t => t.Id)
        .ToListAsync();
    var requestIds = await db.PurchaseRequests
        .Where(r => r.Title.StartsWith("Demo ") || r.Title.StartsWith("Training: ") || r.Title.Contains("guided demo"))
        .Select(r => r.Id)
        .ToListAsync();
    var userIds = await db.Users
        .Where(u => u.Email != null && u.Email.StartsWith("demo.") && u.Email.EndsWith("@orange.local"))
        .Select(u => u.Id)
        .ToListAsync();

    var roundIds = await db.TenderRounds.Where(r => tenderIds.Contains(r.TenderId)).Select(r => r.Id).ToListAsync();
    var reportIds = await db.TenderReports.Where(r => roundIds.Contains(r.TenderRoundId)).Select(r => r.Id).ToListAsync();
    var invitationIds = await db.TenderInvitations.Where(i => roundIds.Contains(i.TenderRoundId)).Select(i => i.Id).ToListAsync();
    var bidIds = await db.Bids.Where(b => invitationIds.Contains(b.TenderInvitationId)).Select(b => b.Id).ToListAsync();
    var committeeRoundIds = await db.CommitteeRounds.Where(c => roundIds.Contains(c.TenderRoundId)).Select(c => c.Id).ToListAsync();
    var criterionIds = await db.EvaluationCriteria.Where(c => committeeRoundIds.Contains(c.CommitteeRoundId)).Select(c => c.Id).ToListAsync();
    var committeeMemberIds = await db.CommitteeMembers.Where(c => tenderIds.Contains(c.TenderId)).Select(c => c.Id).ToListAsync();
    var clarificationIds = await db.Clarifications
        .Where(c => (c.TenderId.HasValue && tenderIds.Contains(c.TenderId.Value)) ||
                    (c.PurchaseRequestId.HasValue && requestIds.Contains(c.PurchaseRequestId.Value)) ||
                    (c.BidId.HasValue && bidIds.Contains(c.BidId.Value)))
        .Select(c => c.Id)
        .ToListAsync();
    var clarificationMessageIds = await db.ClarificationMessages
        .Where(m => clarificationIds.Contains(m.ClarificationId))
        .Select(m => m.Id)
        .ToListAsync();

    await db.Documents.Where(d =>
            (d.TenderReportId.HasValue && reportIds.Contains(d.TenderReportId.Value)) ||
            (d.TenderId.HasValue && tenderIds.Contains(d.TenderId.Value)) ||
            (d.PurchaseRequestId.HasValue && requestIds.Contains(d.PurchaseRequestId.Value)) ||
            (d.BidId.HasValue && bidIds.Contains(d.BidId.Value)) ||
            (d.ClarificationMessageId.HasValue && clarificationMessageIds.Contains(d.ClarificationMessageId.Value)) ||
            userIds.Contains(d.UploadedByUserId))
        .ExecuteDeleteAsync();
    await db.Notifications.Where(n => userIds.Contains(n.UserId)).ExecuteDeleteAsync();
    await db.AuditLogs.Where(a => a.UserId != null && userIds.Contains(a.UserId)).ExecuteDeleteAsync();
    await db.ClarificationMessages.Where(m => clarificationIds.Contains(m.ClarificationId)).ExecuteDeleteAsync();
    await db.Clarifications.Where(c => clarificationIds.Contains(c.Id)).ExecuteDeleteAsync();
    await db.EvaluationScores.Where(s => criterionIds.Contains(s.EvaluationCriterionId) || bidIds.Contains(s.BidId) || committeeMemberIds.Contains(s.CommitteeMemberId)).ExecuteDeleteAsync();
    await db.CommitteeVotes.Where(v => committeeRoundIds.Contains(v.CommitteeRoundId) || committeeMemberIds.Contains(v.CommitteeMemberId)).ExecuteDeleteAsync();
    await db.EvaluationCriteria.Where(c => committeeRoundIds.Contains(c.CommitteeRoundId)).ExecuteDeleteAsync();
    await db.CommitteeRounds.Where(c => committeeRoundIds.Contains(c.Id))
        .ExecuteUpdateAsync(updates => updates.SetProperty(c => c.TenderReportId, (int?)null));
    await db.Tenders.Where(t => tenderIds.Contains(t.Id))
        .ExecuteUpdateAsync(updates => updates.SetProperty(t => t.AwardedBidId, (int?)null));
    await db.CommitteeRounds.Where(c => committeeRoundIds.Contains(c.Id)).ExecuteDeleteAsync();
    await db.TenderReports.Where(r => reportIds.Contains(r.Id)).ExecuteDeleteAsync();
    await db.BidItems.Where(i => bidIds.Contains(i.BidId)).ExecuteDeleteAsync();
    await db.Bids.Where(b => bidIds.Contains(b.Id)).ExecuteDeleteAsync();
    await db.TenderInvitations.Where(i => invitationIds.Contains(i.Id)).ExecuteDeleteAsync();
    await db.CommitteeMembers.Where(m => tenderIds.Contains(m.TenderId)).ExecuteDeleteAsync();
    await db.TenderItems.Where(i => tenderIds.Contains(i.TenderId)).ExecuteDeleteAsync();
    await db.TenderRounds.Where(r => roundIds.Contains(r.Id)).ExecuteDeleteAsync();
    await db.Tenders.Where(t => tenderIds.Contains(t.Id)).ExecuteDeleteAsync();
    await db.PurchaseRequestReviews.Where(r => requestIds.Contains(r.PurchaseRequestId)).ExecuteDeleteAsync();
    await db.PurchaseRequestItems.Where(i => requestIds.Contains(i.PurchaseRequestId)).ExecuteDeleteAsync();
    await db.PurchaseRequests.Where(r => requestIds.Contains(r.Id)).ExecuteDeleteAsync();
    await db.Suppliers.Where(s => s.RegistrationNumber.StartsWith("DEMO-") || s.CompanyName.StartsWith("Demo ")).ExecuteDeleteAsync();
    await db.Departments.Where(d => d.Name == "Demo Sourcing Department" || d.Name == departmentName).ExecuteDeleteAsync();

    await db.UserRoles.Where(r => userIds.Contains(r.UserId)).ExecuteDeleteAsync();
    await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
}

async Task<TenderReport> CreateTrainingScenario(
    ApplicationUser officerUser,
    IReadOnlyList<ApplicationUser> committeeUsers)
{
    var now = DateTime.UtcNow;
    var department = new Department
    {
        Name = departmentName,
        Description = "Training-only data for the Sourcing Manager workflow.",
        IsActive = true
    };
    db.Departments.Add(department);
    await db.SaveChangesAsync();

    var request = new PurchaseRequest
    {
        DepartmentId = department.Id,
        CreatedByUserId = officerUser.Id,
        Title = "Training: Office furniture request",
        Justification = "Replace chairs and desks for the customer-service team.",
        Priority = RequestPriority.Normal,
        NeededBy = now.AddDays(30),
        Status = PurchaseRequestStatus.Accepted,
        CreatedAt = now.AddDays(-14),
        SubmittedAt = now.AddDays(-14)
    };
    db.PurchaseRequests.Add(request);
    await db.SaveChangesAsync();

    var tender = new Tender
    {
        PurchaseRequestId = request.Id,
        CreatedByUserId = officerUser.Id,
        Title = tenderTitle,
        Description = "Purchase ergonomic chairs and work desks for the customer-service team.",
        Requirements = "Two-year warranty, delivery within 21 days, and assembly included.",
        Status = TenderStatus.ManagerReview,
        CreatedAt = now.AddDays(-12)
    };
    db.Tenders.Add(tender);
    await db.SaveChangesAsync();

    var chair = new TenderItem { TenderId = tender.Id, Name = "Ergonomic office chair", Specifications = "Adjustable lumbar support", Quantity = 25, Unit = "Each" };
    var desk = new TenderItem { TenderId = tender.Id, Name = "Work desk", Specifications = "120 cm desk with cable management", Quantity = 12, Unit = "Each" };
    var round = new TenderRound
    {
        TenderId = tender.Id,
        RoundNumber = 1,
        Status = TenderRoundStatus.SentToEvaluation,
        CreatedByUserId = officerUser.Id,
        OpensAt = now.AddDays(-10),
        Deadline = now.AddDays(10),
        CreatedAt = now.AddDays(-10)
    };
    db.TenderItems.AddRange(chair, desk);
    db.TenderRounds.Add(round);
    await db.SaveChangesAsync();

    var suppliers = new[]
    {
        new Supplier { CompanyName = "Amman Office Solutions", RegistrationNumber = "DEMO-OFFICE-001", Email = "sales@ammanoffice.demo", Phone = "+962790000101", Address = "Amman", Status = SupplierStatus.Approved, CreatedAt = now.AddDays(-20) },
        new Supplier { CompanyName = "Capital Workspace", RegistrationNumber = "DEMO-OFFICE-002", Email = "sales@capitalworkspace.demo", Phone = "+962790000102", Address = "Amman", Status = SupplierStatus.Approved, CreatedAt = now.AddDays(-20) },
        new Supplier { CompanyName = "Jordan Modern Furniture", RegistrationNumber = "DEMO-OFFICE-003", Email = "sales@jmf.demo", Phone = "+962790000103", Address = "Zarqa", Status = SupplierStatus.Approved, CreatedAt = now.AddDays(-20) }
    };
    db.Suppliers.AddRange(suppliers);
    await db.SaveChangesAsync();

    var bids = new List<Bid>();
    var offers = new[]
    {
        (suppliers[0], 350m, 480m, 93.25m),
        (suppliers[1], 370m, 500m, 89.50m),
        (suppliers[2], 395m, 520m, 82.00m)
    };
    foreach (var (supplier, chairPrice, deskPrice, _) in offers)
    {
        var invitation = new TenderInvitation
        {
            TenderRoundId = round.Id,
            SupplierId = supplier.Id,
            InvitedByUserId = officerUser.Id,
            Status = InvitationStatus.Accepted,
            InvitedAt = now.AddDays(-9),
            RespondedAt = now.AddDays(-8)
        };
        db.TenderInvitations.Add(invitation);
        await db.SaveChangesAsync();

        var bid = new Bid
        {
            TenderInvitationId = invitation.Id,
            RevisionNumber = 1,
            Status = BidStatus.Submitted,
            CurrencyCode = "JOD",
            DeliveryDays = 18,
            PaymentTerms = "30 days after delivery",
            ShippingCost = 250,
            ComplianceStatus = ComplianceStatus.Compliant,
            ComplianceNotes = "All mandatory requirements were met.",
            CreatedAt = now.AddDays(-7),
            SubmittedAt = now.AddDays(-7)
        };
        db.Bids.Add(bid);
        await db.SaveChangesAsync();
        db.BidItems.AddRange(
            new BidItem { BidId = bid.Id, TenderItemId = chair.Id, Quantity = 25, UnitPrice = chairPrice, OfferedSpecifications = "Ergonomic chair with adjustable lumbar support" },
            new BidItem { BidId = bid.Id, TenderItemId = desk.Id, Quantity = 12, UnitPrice = deskPrice, OfferedSpecifications = "120 cm work desk with cable management" });
        bids.Add(bid);
    }

    foreach (var user in committeeUsers)
        db.CommitteeMembers.Add(new CommitteeMember { TenderId = tender.Id, UserId = user.Id, CommitteeType = CommitteeType.Top, AssignedAt = now });

    var ranking = string.Join(",", offers.Select((offer, index) =>
        $"{{\"rank\":{index + 1},\"supplier\":\"{offer.Item1.CompanyName}\",\"finalScore\":{offer.Item4:0.00},\"eligible\":true}}"));
    var report = new TenderReport
    {
        TenderRoundId = round.Id,
        ReportType = ReportType.FullEvaluation,
        VersionNumber = 1,
        Content = "Training scenario: office furniture evaluation ready for Sourcing Manager review.",
        RecommendedBidId = bids[0].Id,
        RankingSnapshotJson = "[" + ranking + "]",
        Status = ReportStatus.Submitted,
        CreatedByUserId = officerUser.Id,
        CreatedAt = now.AddDays(-1),
        SubmittedAt = now.AddDays(-1)
    };
    db.TenderReports.Add(report);
    await db.SaveChangesAsync();
    return report;
}

async Task EnsureRole(string name)
{
    if (!await db.Roles.AnyAsync(role => role.Name == name))
    {
        db.Roles.Add(new IdentityRole(name));
        await db.SaveChangesAsync();
    }
}

async Task<ApplicationUser> EnsureUser(string email, string fullName, string userPassword, string role)
{
    var user = await db.Users.SingleOrDefaultAsync(existing => existing.Email == email);
    if (user == null)
    {
        user = new ApplicationUser();
        db.Users.Add(user);
    }

    user.UserName = email;
    user.NormalizedUserName = email.ToUpperInvariant();
    user.Email = email;
    user.NormalizedEmail = email.ToUpperInvariant();
    user.EmailConfirmed = true;
    user.FullName = fullName;
    user.IsActive = true;
    user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, userPassword);
    await db.SaveChangesAsync();

    var roleId = await db.Roles.Where(existing => existing.Name == role).Select(existing => existing.Id).SingleAsync();
    if (!await db.UserRoles.AnyAsync(existing => existing.UserId == user.Id && existing.RoleId == roleId))
    {
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    return user;
}

async Task EnsureManagerNotification(string managerId, int reportId)
{
    var link = $"/SourcingManager/Reports/Review?id={reportId}";
    if (await db.Notifications.AnyAsync(n => n.UserId == managerId && n.Link == link))
        return;

    db.Notifications.Add(new Notification
    {
        UserId = managerId,
        Title = "Office furniture report ready for review",
        Message = "Three supplier offers were evaluated. Review the recommended supplier and make your decision.",
        Link = link,
        IsRead = false,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync();
}
