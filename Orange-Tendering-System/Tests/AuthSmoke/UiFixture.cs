using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

// Isolated UI test fixture: one account per role with known test passwords (written only to the private state file),
// plus a small set of real rows (requests, tenders, invitations, offers, clarifications, notifications) so the
// dashboards, tables and the notification bell have genuine SQL Server data to show. Never loaded by the application.
public static class UiFixture
{
    public static async Task Run()
    {
        var connection = Environment.GetEnvironmentVariable("UI_FIXTURE_CONNECTION") ?? throw new InvalidOperationException("UI_FIXTURE_CONNECTION required.");
        if (!new SqlConnectionStringBuilder(connection).InitialCatalog.StartsWith("OrangeAuthCheck_UiE2E_", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Isolated OrangeAuthCheck_UiE2E_ database required.");
        var statePath = Environment.GetEnvironmentVariable("UI_FIXTURE_STATE") ?? throw new InvalidOperationException("UI_FIXTURE_STATE required.");
        var root = Environment.GetEnvironmentVariable("AUTH_PROJECT_ROOT") ?? throw new InvalidOperationException("AUTH_PROJECT_ROOT required.");
        var suffix = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
        string Pw() => "Ui!9" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
        var adminEmail = $"admin-{suffix}@example.test"; var adminPw = Pw();
        Environment.SetEnvironmentVariable("ConnectionStrings__Default Connection", connection);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", adminEmail);
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", adminPw);

        using var factory = new WebApplicationFactory<WebAppMarker>().WithWebHostBuilder(b =>
        {
            b.UseContentRoot(root).UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Default Connection"] = connection, ["BootstrapAdmin:Email"] = adminEmail, ["BootstrapAdmin:Password"] = adminPw }));
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTime.UtcNow;
        var accounts = new JsonArray();

        var admin = (await users.FindByEmailAsync(adminEmail))!;
        admin.MustChangePassword = false; admin.FullName = "Layla Admin"; await users.UpdateAsync(admin);
        accounts.Add(new JsonObject { ["role"] = SystemRoles.Admin, ["email"] = adminEmail, ["password"] = adminPw, ["area"] = "Admin" });

        var dept = new Department { Name = "Information Technology", Description = "IT department" };
        db.Departments.Add(dept); await db.SaveChangesAsync();

        async Task<ApplicationUser> Make(string role, string area, string name, int? department = null, int? supplierId = null)
        {
            var email = $"{area.ToLowerInvariant()}-{suffix}@example.test"; var pw = Pw();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true, FullName = name, DepartmentId = department, SupplierId = supplierId };
            if (!(await users.CreateAsync(user, pw)).Succeeded || !(await users.AddToRoleAsync(user, role)).Succeeded) throw new InvalidOperationException("Could not create " + role);
            accounts.Add(new JsonObject { ["role"] = role, ["email"] = email, ["password"] = pw, ["area"] = area });
            return user;
        }
        var requester = await Make(SystemRoles.RequestingDepartment, "Department", "Omar Requester", dept.Id);
        var officer = await Make(SystemRoles.SourcingOfficer, "SourcingOfficer", "Sara Officer");
        var evaluator = await Make(SystemRoles.EvaluationCommittee, "EvaluationCommittee", "Khaled Evaluator", dept.Id);
        var manager = await Make(SystemRoles.SourcingManager, "SourcingManager", "Nour Manager");
        var top = await Make(SystemRoles.TopCommittee, "TopCommittee", "Yousef Committee");

        var s1 = new Supplier { CompanyName = "TechWorld Supplier", RegistrationNumber = "CR-" + suffix + "1", Email = $"techworld-{suffix}@example.test", Status = SupplierStatus.Approved, CreatedAt = now.AddDays(-60), ReviewedAt = now.AddDays(-59), ReviewedByUserId = admin.Id };
        var s2 = new Supplier { CompanyName = "Gulf Systems Co.", RegistrationNumber = "CR-" + suffix + "2", Email = $"gulf-{suffix}@example.test", Status = SupplierStatus.Approved, CreatedAt = now.AddDays(-50), ReviewedAt = now.AddDays(-49), ReviewedByUserId = admin.Id };
        var s3 = new Supplier { CompanyName = "Pending Traders", RegistrationNumber = "CR-" + suffix + "3", Email = $"pending-{suffix}@example.test", Status = SupplierStatus.Pending, CreatedAt = now.AddDays(-2) };
        db.Suppliers.AddRange(s1, s2, s3); await db.SaveChangesAsync();
        var sup1 = await Make(SystemRoles.Supplier, "Supplier", "TechWorld Contact", supplierId: s1.Id);
        var sup2 = await Make(SystemRoles.Supplier, "SupplierTwo", "Gulf Contact", supplierId: s2.Id);

        PurchaseRequest Pr(string title, PurchaseRequestStatus status, int daysAgo) => new()
        {
            DepartmentId = dept.Id, CreatedByUserId = requester.Id, Title = title, Justification = "Operational need confirmed by the department head.",
            Priority = RequestPriority.High, NeededBy = now.AddDays(45), Status = status, CreatedAt = now.AddDays(-daysAgo), SubmittedAt = status == PurchaseRequestStatus.Draft ? null : now.AddDays(-daysAgo),
            Items = { new PurchaseRequestItem { Name = title, Quantity = 10, Unit = "pcs", Specifications = "As described in the request" } }
        };
        var pr1 = Pr("Network Equipment Procurement", PurchaseRequestStatus.Accepted, 20);
        var pr2 = Pr("IT Licenses and Subscriptions", PurchaseRequestStatus.Accepted, 30);
        var pr3 = Pr("Office Laptops", PurchaseRequestStatus.Submitted, 3);
        var pr4 = Pr("Conference Room Displays", PurchaseRequestStatus.Draft, 1);
        var pr5 = Pr("Site Maintenance Services", PurchaseRequestStatus.Accepted, 90);
        db.PurchaseRequests.AddRange(pr1, pr2, pr3, pr4, pr5); await db.SaveChangesAsync();

        Tender Tender(PurchaseRequest pr, TenderStatus status, int daysAgo, decimal value) => new()
        {
            PurchaseRequestId = pr.Id, Title = pr.Title, Description = "Tender for " + pr.Title, Requirements = "Compliance with listed technical requirements.",
            Status = status, CreatedByUserId = officer.Id, CreatedAt = now.AddDays(-daysAgo), EstimatedValue = value
        };
        var t1 = Tender(pr1, TenderStatus.Bidding, 10, 48000); var t2 = Tender(pr2, TenderStatus.Evaluation, 25, 22000); var t3 = Tender(pr5, TenderStatus.Awarded, 80, 15000);
        db.Tenders.AddRange(t1, t2, t3); await db.SaveChangesAsync();
        TenderRound Round(Tender t, TenderRoundStatus s, DateTime opens, DateTime deadline) => new() { TenderId = t.Id, RoundNumber = 1, Status = s, OpensAt = opens, Deadline = deadline, CreatedByUserId = officer.Id, CreatedAt = opens };
        var r1 = Round(t1, TenderRoundStatus.Open, now.AddDays(-9), now.AddDays(5)); var r2 = Round(t2, TenderRoundStatus.SentToEvaluation, now.AddDays(-24), now.AddDays(-12)); var r3 = Round(t3, TenderRoundStatus.Closed, now.AddDays(-78), now.AddDays(-60));
        db.TenderRounds.AddRange(r1, r2, r3); await db.SaveChangesAsync();
        TenderInvitation Inv(TenderRound r, Supplier s, InvitationStatus st) => new() { TenderRoundId = r.Id, SupplierId = s.Id, Status = st, InvitedByUserId = officer.Id, InvitedAt = r.OpensAt!.Value, RespondedAt = st == InvitationStatus.Pending ? null : r.OpensAt!.Value.AddDays(1) };
        var i11 = Inv(r1, s1, InvitationStatus.Pending); var i12 = Inv(r1, s2, InvitationStatus.Accepted);
        var i21 = Inv(r2, s1, InvitationStatus.Accepted); var i22 = Inv(r2, s2, InvitationStatus.Accepted);
        var i31 = Inv(r3, s1, InvitationStatus.Accepted);
        db.TenderInvitations.AddRange(i11, i12, i21, i22, i31); await db.SaveChangesAsync();
        Bid B(TenderInvitation i, BidStatus st, int delivery, int daysAgo) => new() { TenderInvitationId = i.Id, Status = st, TechnicalProposal = "Full technical proposal.", DeliveryDays = delivery, PaymentTerms = "30 days", ValidUntil = now.AddDays(60), CreatedAt = now.AddDays(-daysAgo - 1), SubmittedAt = st == BidStatus.Submitted ? now.AddDays(-daysAgo) : null };
        var b12 = B(i12, BidStatus.Submitted, 21, 7); var b21 = B(i21, BidStatus.Submitted, 14, 38); var b22 = B(i22, BidStatus.Submitted, 30, 37); var b31 = B(i31, BidStatus.Submitted, 10, 95);
        db.Bids.AddRange(b12, b21, b22, b31); await db.SaveChangesAsync();
        t3.AwardedBidId = b31.Id; t3.AwardedAt = now.AddDays(-50); t3.AwardedByUserId = top.Id; t3.ClosedAt = now.AddDays(-50);
        db.CommitteeMembers.Add(new CommitteeMember { TenderId = t2.Id, CommitteeType = CommitteeType.Evaluation, Responsibility = CommitteeResponsibility.TechnicalRepresentative, UserId = evaluator.Id, AssignedAt = now.AddDays(-20) });
        db.Clarifications.Add(new Clarification { TenderId = t1.Id, FromUserId = sup2.Id, ToUserId = officer.Id, Subject = "Question about delivery site", Status = ClarificationStatus.Open, CreatedAt = now.AddHours(-5) });

        Notification N(ApplicationUser u, string title, string message, string link, bool read, int minutesAgo) => new() { UserId = u.Id, Title = title, Message = message, Link = link, IsRead = read, CreatedAt = now.AddMinutes(-minutesAgo) };
        db.Notifications.AddRange(
            N(officer, "New offer submitted", "Gulf Systems Co. submitted an offer for Network Equipment Procurement.", $"/Workflow/Details/{t1.Id}", false, 25),
            N(officer, "Clarification received", "Gulf Systems Co. asked about the delivery site.", $"/Workflow/Details/{t1.Id}", false, 300),
            N(officer, "Evaluation started", "Evaluation started for IT Licenses and Subscriptions.", $"/Workflow/Details/{t2.Id}", true, 1500),
            N(sup1, "Tender invitation", "You were invited to Network Equipment Procurement.", "/Supplier/Invitations", false, 40),
            N(sup1, "Tender result", "Site Maintenance Services was awarded to your company.", "/Supplier/Offers", true, 6000),
            N(sup2, "Offer received", "Your offer for Network Equipment Procurement was received.", "/Supplier/Offers", false, 120),
            N(requester, "Request accepted", "Network Equipment Procurement was accepted by sourcing.", "/Department/PurchaseRequests", false, 700),
            N(manager, "Report awaiting review", "The evaluation report for IT Licenses is ready for review.", $"/Workflow/Details/{t2.Id}", false, 90),
            N(top, "Decision required", "A critical tender is waiting for the committee decision.", $"/Workflow/Details/{t2.Id}", false, 200),
            N(evaluator, "Scoring window open", "Please score the offers for IT Licenses and Subscriptions.", $"/Workflow/Details/{t2.Id}", false, 60),
            N(admin, "Supplier registration", "Pending Traders applied for approval.", "/Admin/Suppliers", false, 15));
        db.AuditLogs.AddRange(
            new AuditLog { UserId = officer.Id, Action = "TenderPublished", EntityName = "Tender", EntityId = t1.Id.ToString(), CreatedAt = now.AddDays(-9) },
            new AuditLog { UserId = admin.Id, Action = "SupplierApproved", EntityName = "Supplier", EntityId = s1.Id.ToString(), CreatedAt = now.AddDays(-59) },
            new AuditLog { UserId = requester.Id, Action = "RequestSubmitted", EntityName = "PurchaseRequest", EntityId = pr3.Id.ToString(), CreatedAt = now.AddDays(-3) });
        await db.SaveChangesAsync();

        await File.WriteAllTextAsync(statePath, new JsonObject { ["connection"] = connection, ["accounts"] = accounts, ["tenderId"] = t1.Id }.ToJsonString());
        Console.WriteLine("UI fixture ready: " + accounts.Count + " accounts, 3 tenders, 5 requests, 2 suppliers + 1 pending.");
    }
}
