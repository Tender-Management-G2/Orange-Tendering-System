using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Radar;

// Clearly labeled, fictional ORANGE RADAR demonstration dataset ("RADAR DEMO ..." suppliers, tenders and departments).
// It exists so every analytics path can be exercised on a realistic small market: normal competition, repeated participation,
// shared contacts, a stable price gap, near-identical list prices, an identical file, legitimately similar (template) text,
// award rotation, a sealed open round and insufficient-data cases. Nothing here is a finding: the real detectors find them.
// Guard: Development + OrangeRadar:DemoDataset:Enabled + a database whose name ends with "_RadarDemo". The shared team database
// (OrangeTenderingTeamDev) and any other database are never touched.
public static class RadarDemoSeeder
{
    public const string Prefix = "RADAR DEMO ";

    public static bool IsDemoDatabase(string? connectionString) =>
        !string.IsNullOrEmpty(connectionString) && new SqlConnectionStringBuilder(connectionString).InitialCatalog.EndsWith("_RadarDemo", StringComparison.OrdinalIgnoreCase);

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var environment = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var connection = db.Database.GetConnectionString();
        if (!TeamDevDatabase.IsLocalTeamDatabase(connection) || !IsDemoDatabase(connection))
        {
            logger.LogWarning("OrangeRadar:DemoDataset:Enabled ignored: the demo dataset requires a local team database named *_RadarDemo.");
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = N'OrangeRadarDemoSeed', @LockMode = N'Exclusive', @LockTimeout = 30000;
            IF @result < 0 THROW 50001, 'Could not lock Orange Radar demo seed data.', 1;
            """);

        var hasOriginalMarket = await db.Suppliers.AnyAsync(s => s.RegistrationNumber == "RADAR-DEMO-001");
        var hasIntelligenceSuppliers = await db.Suppliers.AnyAsync(s => s.RegistrationNumber == "RADAR-DEMO-INT-001");
        if (hasOriginalMarket && hasIntelligenceSuppliers) return;

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var officer = await users.FindByEmailAsync(TeamAccountSeeder.Find(SystemRoles.SourcingOfficer).Email) ?? throw new InvalidOperationException("Team officer account missing.");
        var requester = await users.FindByEmailAsync(TeamAccountSeeder.Find(SystemRoles.RequestingDepartment).Email) ?? officer;

        if (!hasOriginalMarket)
            await SeedOriginalMarketAsync(db, environment, officer, requester, logger);
        if (!hasIntelligenceSuppliers)
            await SeedIntelligenceSuppliersAsync(db, officer, requester, logger);

        await transaction.CommitAsync();
    }

    private static async Task SeedOriginalMarketAsync(ApplicationDbContext db, IWebHostEnvironment environment,
        ApplicationUser officer, ApplicationUser requester, ILogger logger)
    {
        var it = new Department { Name = Prefix + "- Information Technology", Description = "Fictional Orange Radar demonstration department." };
        var admin = new Department { Name = Prefix + "- Administration", Description = "Fictional Orange Radar demonstration department." };
        db.Departments.AddRange(it, admin);

        var number = 0;
        Supplier S(string name, string category, string email, string phone, string address, SupplierStatus status = SupplierStatus.Approved) => new()
        {
            CompanyName = Prefix + name, Category = category, Email = email, Phone = phone, Address = address, Status = status,
            RegistrationNumber = $"RADAR-DEMO-{++number:000}", TaxNumber = $"RD-TAX-{4100 + number * 37}",
            CreatedAt = new DateTime(2025, 11, 3, 9, 0, 0, DateTimeKind.Utc), ReviewedAt = new DateTime(2025, 11, 10, 9, 0, 0, DateTimeKind.Utc), ReviewNotes = "Fictional demonstration supplier."
        };
        var IT = SupplierCategories.InformationTechnology;
        var atlas = S("Atlas Systems", IT, "bids@atlas-systems.example", "+962 6 410 2201", "Mecca Street 88, Amman");
        var petra = S("Petra Tech Supplies", IT, "tenders@petratech.example", "+962 6 420 7713", "Wasfi Al Tal Street 15, Amman");
        var wadi = S("Wadi Digital", IT, "sales@wadidigital.example", "+962 6 555 0142", "Building 12, King Abdullah II Street, Amman");
        var aqaba = S("Aqaba Network Co", IT, "office@aqabanet.example", "+962 6 555 0142", "Building 12 - King Abdullah II Street - Amman");
        var jerash = S("Jerash Office Hub", SupplierCategories.OfficeSupplies, "orders@jerashoffice.example", "+962 6 430 1180", "Business Park Tower B, Floor 3, Amman");
        var madaba = S("Madaba Furnishings", SupplierCategories.Furniture, "info@madabafurn.example", "+962 5 324 8890", "Industrial Zone 4, Madaba");
        var zarqa = S("Zarqa Trading", SupplierCategories.OfficeSupplies, "trade@zarqatrading.example", "+962 5 399 4021", "Business Park Tower B Floor 3 Amman");
        var irbid = S("Irbid Solutions", IT, "rfq@irbidsolutions.example", "+962 2 725 6610", "University Street 40, Irbid");
        var salt = S("Salt Logistics", SupplierCategories.Transport, "ops@saltlogistics.example", "+962 5 355 7002", "Highway 30, Salt");
        var karak = S("Karak Supplies", SupplierCategories.OfficeSupplies, "hello@karaksupplies.example", "+962 3 238 1150", "Castle Road 5, Karak");
        var ajloun = S("Ajloun Furniture", SupplierCategories.Furniture, "ajloun@ajlounfurniture.example", "+962 2 642 0099", "Main Street 3, Ajloun", SupplierStatus.Pending);
        var mafraq = S("Mafraq Digital", IT, "mafraq@mafraqdigital.example", "+962 2 623 4471", "Station Road 7, Mafraq", SupplierStatus.Suspended);
        var suppliers = new[] { atlas, petra, wadi, aqaba, jerash, madaba, zarqa, irbid, salt, karak, ajloun, mafraq };
        db.Suppliers.AddRange(suppliers);
        await db.SaveChangesAsync();

        var docRoot = Path.Combine(environment.ContentRootPath, "App_Data", "TenderDocuments");
        Directory.CreateDirectory(docRoot);
        Document File(string name, string content, int? tenderId = null, int? bidId = null, int? supplierId = null, DocumentVerificationStatus? verification = null)
        {
            var key = "radar-demo-" + Guid.NewGuid().ToString("N") + Path.GetExtension(name);
            System.IO.File.WriteAllText(Path.Combine(docRoot, key), content, new UTF8Encoding(false));
            return new Document { FileName = name, StoragePath = key, ContentType = "text/plain", SizeBytes = Encoding.UTF8.GetByteCount(content), UploadedByUserId = officer.Id,
                UploadedAt = new DateTime(2025, 11, 12, 10, 0, 0, DateTimeKind.Utc), TenderId = tenderId, BidId = bidId, SupplierId = supplierId, VerificationStatus = verification,
                DocumentType = supplierId != null ? "CommercialRegistration" : bidId != null ? "TechnicalProposal" : "TenderRequirements" };
        }
        foreach (var s in suppliers.Take(9))
            db.Documents.Add(File($"commercial-registration-{s.Id}.txt", $"Commercial registration certificate (fictional demo) for {s.CompanyName}, registration {s.RegistrationNumber}.",
                supplierId: s.Id, verification: s == salt ? DocumentVerificationStatus.Pending : DocumentVerificationStatus.Verified));

        const string RequirementsText = "Mandatory requirements: The supplier shall deliver all equipment to the Orange headquarters in Amman within 30 calendar days of the purchase order. All devices must carry a minimum three-year manufacturer warranty with on-site support. The supplier shall provide a valid tax certificate, commercial registration and ISO 9001 certificate. Payment terms are 60 days after acceptance. Prices must be quoted in Jordanian dinars including delivery and installation.";

        // ---- Tenders: (title, department, month, status, items(name, unit, qty, base price), bids(supplier, factor, minute offset, compliance), award supplier)
        var tenderCount = 0;
        async Task<(Tender, TenderRound, Dictionary<Supplier, Bid>)> AddTender(string title, Department dept, DateTime deadline, TenderStatus status, TenderRoundStatus roundStatus,
            (string Name, string Unit, decimal Qty, decimal Price)[] items, (Supplier Supplier, decimal Factor, int Minutes, ComplianceStatus Compliance)[] offers,
            Supplier[] invitedOnly, Supplier? winner, string? requirements = null)
        {
            var tender = await AddTenderAsync(db, officer, requester, title, dept, deadline, status, roundStatus, items, offers, invitedOnly, winner, requirements);
            tenderCount++;
            return tender;
        }

        DateTime D(int month, int day) => new(2026, month, day, 11, 0, 0, DateTimeKind.Utc);
        var laptops = new[] { ("Business laptop", "unit", 40m, 820m), ("Docking station", "unit", 40m, 145m), ("27-inch monitor", "unit", 40m, 210m) };
        var network = new[] { ("48-port access switch", "unit", 6m, 2350m), ("Wireless access point", "unit", 24m, 310m), ("Installation service", "lot", 1m, 4200m) };
        var servers = new[] { ("Rack server", "unit", 4m, 6900m), ("Storage array", "unit", 1m, 18400m), ("UPS 10kVA", "unit", 2m, 3100m) };
        var firewalls = new[] { ("Next-generation firewall", "unit", 2m, 8700m), ("Security licence (3 years)", "lot", 1m, 5200m), ("Configuration service", "lot", 1m, 2600m) };
        var printers = new[] { ("A3 multifunction printer", "unit", 8m, 1950m), ("Toner set", "set", 40m, 96m), ("Maintenance contract", "lot", 1m, 1500m) };

        // IT market: Petra and Wadi alternate as the lowest bidder; Aqaba always prices ~4% above Wadi and submits minutes after it;
        // Irbid matches Atlas' (published) list prices in two rounds; Atlas is an ordinary competitor.
        var (t1, _, _) = await AddTender("Staff laptops refresh", it, D(1, 20), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, laptops,
            [(atlas, 1.05m, 0, ComplianceStatus.Compliant), (petra, 0.95m, 600, ComplianceStatus.Compliant), (wadi, 1.02m, 900, ComplianceStatus.Compliant), (aqaba, 1.0608m, 904, ComplianceStatus.Compliant)], [mafraq], petra);
        var (t2, r2, b2) = await AddTender("Campus network upgrade", it, D(3, 18), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, network,
            [(atlas, 1.04m, 120, ComplianceStatus.Compliant), (petra, 1.03m, 1500, ComplianceStatus.Compliant), (wadi, 0.96m, 300, ComplianceStatus.Compliant), (aqaba, 0.99936m, 306, ComplianceStatus.Compliant), (irbid, 1.07m, 2000, ComplianceStatus.Compliant)], [], wadi, RequirementsText);
        var (t3, _, b3) = await AddTender("Data centre servers", it, D(5, 21), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, servers,
            [(petra, 0.94m, 200, ComplianceStatus.Compliant), (wadi, 1.01m, 1100, ComplianceStatus.Compliant), (aqaba, 1.05141m, 1103, ComplianceStatus.NonCompliant)], [atlas], petra);
        var (t4, _, _) = await AddTender("Perimeter firewalls", it, D(7, 16), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, firewalls,
            [(atlas, 1.03m, 60, ComplianceStatus.Compliant), (petra, 1.02m, 1400, ComplianceStatus.Compliant), (wadi, 0.95m, 700, ComplianceStatus.Compliant), (aqaba, 0.988m, 705, ComplianceStatus.Compliant), (irbid, 1.03m, 2500, ComplianceStatus.Compliant)], [], wadi);
        await AddTender("Meeting room displays", it, D(9, 24), TenderStatus.Evaluation, TenderRoundStatus.SentToEvaluation, laptops,
            [(atlas, 1.0m, 30, ComplianceStatus.Compliant), (wadi, 0.99m, 800, ComplianceStatus.Compliant), (aqaba, 1.03158m, 803, ComplianceStatus.Compliant), (irbid, 1.0m, 1900, ComplianceStatus.Compliant)], [petra], null);
        // Office market: two suppliers sharing a business-centre address; low competition.
        await AddTender("Office printers", admin, D(2, 26), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, printers,
            [(jerash, 0.97m, 400, ComplianceStatus.Compliant), (zarqa, 1.04m, 2100, ComplianceStatus.Compliant)], [karak], jerash);
        await AddTender("Stationery framework", admin, D(8, 20), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, [("Copy paper A4", "box", 300m, 21m), ("Stationery kit", "set", 120m, 38m)],
            [(jerash, 1.02m, 100, ComplianceStatus.Compliant), (zarqa, 0.98m, 1700, ComplianceStatus.Compliant)], [karak, salt], zarqa);
        await AddTender("Archive shelving", admin, D(10, 9), TenderStatus.Cancelled, TenderRoundStatus.Failed, [("Mobile archive shelving", "bay", 12m, 1450m)],
            [(jerash, 1.0m, 500, ComplianceStatus.NonCompliant)], [madaba, karak], null);
        await AddTender("Executive furniture", admin, D(6, 12), TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation, [("Executive desk", "unit", 6m, 1150m), ("Ergonomic chair", "unit", 20m, 340m)],
            [(madaba, 1.0m, 900, ComplianceStatus.Compliant)], [ajloun], madaba);
        // Sealed: bidding still open, so Orange Radar must not analyse these two offers.
        await AddTender("Collaboration licences", it, DateTime.UtcNow.Date.AddDays(12), TenderStatus.Bidding, TenderRoundStatus.Open, [("Collaboration suite licence", "user", 250m, 64m)],
            [(atlas, 1.0m, 0, ComplianceStatus.NotChecked), (petra, 0.97m, 30, ComplianceStatus.NotChecked)], [wadi], null);

        // Documents. Tender-issued requirements (template) for the network tender.
        db.Documents.Add(File("network-requirements.txt", RequirementsText, tenderId: t2.Id));
        // Byte-identical technical proposal uploaded by Wadi and Aqaba.
        const string sharedProposal = "Technical proposal - campus network upgrade.\n\nWe will deploy a two-tier switching architecture with redundant uplinks, configure VLAN segmentation per floor and commission 24 Wi-Fi 6 access points after a predictive heat-map survey. Our certified engineers complete the cut-over over two weekends to avoid business disruption, followed by a five-day hypercare period and as-built documentation.";
        db.Documents.Add(File("technical-proposal.txt", sharedProposal, bidId: b2[wadi].Id));
        db.Documents.Add(File("technical-proposal-final.txt", sharedProposal, bidId: b2[aqaba].Id));
        // Legitimately similar: Atlas and Irbid both restate the tender's requirements (template), then describe different methods.
        db.Documents.Add(File("atlas-network-offer.txt", RequirementsText + "\n\nAtlas approach: we use our own Amman logistics hub and a single-vendor stack from one manufacturer. Staging and burn-in of every switch happens in our lab for three days before delivery, and our help desk operates in two shifts.", bidId: b2[atlas].Id));
        db.Documents.Add(File("irbid-network-offer.txt", RequirementsText + "\n\nIrbid approach: equipment ships directly from the regional distributor in Dubai. We subcontract cabling to a licensed local partner and provide remote monitoring from our Irbid network operations centre with quarterly preventive visits.", bidId: b2[irbid].Id));
        // Semantically similar (re-worded) methodology from Petra and Wadi in the server tender.
        db.Documents.Add(File("petra-server-methodology.txt", "Implementation methodology.\n\nOur team will install the rack servers and storage array in the existing data hall, migrate the virtual machines in three controlled waves over consecutive weekends, and validate every workload with the application owners before decommissioning the legacy hosts. A rollback plan is prepared for each wave and daily status reports are shared with Orange IT.", bidId: b3[petra].Id));
        db.Documents.Add(File("wadi-server-approach.txt", "Delivery approach.\n\nWe will mount the new rack servers and the storage array inside the current data hall, move the virtual machines in three planned waves across successive weekends, and confirm each workload with its application owner before the old hosts are retired. Every wave has a prepared rollback plan, and Orange IT receives a status report each day.", bidId: b3[wadi].Id));
        await db.SaveChangesAsync();
        logger.LogInformation("Orange Radar demonstration dataset created: {Suppliers} fictional suppliers, {Tenders} fictional tenders.", suppliers.Length, tenderCount);
    }

    private static async Task SeedIntelligenceSuppliersAsync(ApplicationDbContext db, ApplicationUser officer,
        ApplicationUser requester, ILogger logger)
    {
        var today = DateTime.UtcNow.Date;
        var department = new Department
        {
            Name = Prefix + "- Procurement Services",
            Description = "Fictional Orange Intelligence demonstration department."
        };
        db.Departments.Add(department);

        var suppliers = new List<Supplier>();
        Supplier AddSupplier(string name, string category)
        {
            var emailDomain = name.ToLowerInvariant().Replace(' ', '-');
            var supplier = new Supplier
            {
                CompanyName = Prefix + name,
                Category = category,
                RegistrationNumber = $"RADAR-DEMO-INT-{suppliers.Count + 1:000}",
                Email = $"bids@{emailDomain}.example",
                Status = SupplierStatus.Approved,
                CreatedAt = today.AddDays(-240),
                ReviewedByUserId = officer.Id,
                ReviewedAt = today.AddDays(-230),
                ReviewNotes = "Fictional Orange Intelligence demonstration supplier."
            };
            suppliers.Add(supplier);
            return supplier;
        }

        var nova = AddSupplier("Nova Technology Supply", SupplierCategories.InformationTechnology);
        var cedar = AddSupplier("Cedar Fiber Networks", SupplierCategories.Telecommunications);
        var orbit = AddSupplier("Orbit Connect Services", SupplierCategories.Telecommunications);
        var ledger = AddSupplier("Ledger Cloud Solutions", SupplierCategories.Software);
        var summit = AddSupplier("Summit Operations Advisory", SupplierCategories.ProfessionalServices);
        var volt = AddSupplier("Volt Electrical Supply", SupplierCategories.Electrical);
        var stonebridge = AddSupplier("Stonebridge Contracting", SupplierCategories.Construction);
        var clearway = AddSupplier("Clearway Facilities", SupplierCategories.Facilities);
        var routewell = AddSupplier("Routewell Logistics", SupplierCategories.Transport);
        var caravan = AddSupplier("Caravan Fleet Services", SupplierCategories.Transport);
        var formline = AddSupplier("Formline Workspace", SupplierCategories.Furniture);
        var paperline = AddSupplier("Paperline Business Supplies", SupplierCategories.OfficeSupplies);
        var safeguard = AddSupplier("Safeguard Medical Supply", SupplierCategories.Medical);
        var harvest = AddSupplier("Harvest Table Catering", SupplierCategories.Food);
        var signal = AddSupplier("Signal Creative Studio", SupplierCategories.Marketing);
        db.Suppliers.AddRange(suppliers);
        await db.SaveChangesAsync();

        var tenderCount = 0;
        var offerCount = 0;
        async Task AddTender(string title, int daysAgo, (string Name, string Unit, decimal Qty, decimal Price)[] items,
            (Supplier Supplier, decimal PriceFactor)[] quotes, Supplier? winner = null)
        {
            var offers = quotes.Select((quote, index) =>
                (quote.Supplier, quote.PriceFactor, 90 + index * 241, ComplianceStatus.Compliant)).ToArray();
            await AddTenderAsync(db, officer, requester, title, department, today.AddDays(-daysAgo).AddHours(11),
                winner == null ? TenderStatus.Evaluation : TenderStatus.Awarded, TenderRoundStatus.SentToEvaluation,
                items, offers, [], winner);
            tenderCount++;
            offerCount += quotes.Length;
        }

        await AddTender("Branch fiber connectivity", 122,
            [("Fiber connection installation", "site", 8m, 1150m), ("Managed connectivity", "site-year", 8m, 1860m)],
            [(cedar, 0.94m), (orbit, 1.04m), (nova, 1.12m)], cedar);
        await AddTender("IT service desk licences", 104,
            [("Service desk licence", "agent-year", 55m, 205m), ("System configuration", "lot", 1m, 3600m)],
            [(ledger, 0.91m), (nova, 1.01m), (summit, 1.09m)], ledger);
        await AddTender("Branch electrical retrofit", 86,
            [("Distribution panel replacement", "unit", 4m, 2350m), ("LED fixture with installation", "unit", 90m, 74m)],
            [(volt, 0.96m), (stonebridge, 1.03m), (clearway, 1.13m)], volt);
        await AddTender("Regional equipment transport", 68,
            [("Intercity equipment delivery", "trip", 36m, 310m), ("Pallet handling", "pallet", 144m, 35m)],
            [(routewell, 1.05m), (caravan, 0.93m)], caravan);
        await AddTender("Regional office workstation fit-out", 53,
            [("Desk and chair package", "set", 45m, 580m), ("Workstation assembly", "station", 45m, 48m)],
            [(formline, 0.92m), (paperline, 1.08m), (stonebridge, 1.02m)], formline);
        await AddTender("Workplace first-aid supplies", 37,
            [("Workplace first-aid kit", "kit", 55m, 48m), ("Wall-mounted eye wash station", "unit", 15m, 83m)],
            [(safeguard, 0.89m), (clearway, 1.06m)], safeguard);
        await AddTender("Training workshop catering", 21,
            [("Lunch and refreshments", "person-day", 260m, 18.50m), ("Service setup", "day", 4m, 255m)],
            [(harvest, 0.95m), (summit, 1.11m)]);
        await AddTender("Regional branch launch campaign", 9,
            [("Campaign creative package", "lot", 1m, 7600m), ("Printed outreach kits", "kit", 500m, 8.60m)],
            [(signal, 0.97m), (summit, 1.14m), (paperline, 1.07m)]);

        logger.LogInformation("Orange Intelligence demo data created: {Suppliers} suppliers, {Tenders} tenders and {Offers} offers.",
            suppliers.Count, tenderCount, offerCount);
    }

    private static async Task<(Tender Tender, TenderRound Round, Dictionary<Supplier, Bid> Bids)> AddTenderAsync(
        ApplicationDbContext db, ApplicationUser officer, ApplicationUser requester, string title, Department dept,
        DateTime deadline, TenderStatus status, TenderRoundStatus roundStatus,
        (string Name, string Unit, decimal Qty, decimal Price)[] items,
        (Supplier Supplier, decimal Factor, int Minutes, ComplianceStatus Compliance)[] offers,
        Supplier[] invitedOnly, Supplier? winner, string? requirements = null)
    {
        var pr = new PurchaseRequest { DepartmentId = dept.Id, CreatedByUserId = requester.Id, Title = title, Justification = "Fictional Orange Radar demonstration request.",
            Status = PurchaseRequestStatus.ConvertedToTender, CreatedAt = deadline.AddDays(-40), SubmittedAt = deadline.AddDays(-39) };
        var tender = new Tender { PurchaseRequest = pr, Title = Prefix + title, Description = "Fictional demonstration tender for Orange Radar: " + title + ".", Requirements = requirements,
            Status = status, CreatedByUserId = officer.Id, CreatedAt = deadline.AddDays(-30), Currency = "JOD", EstimatedValue = items.Sum(i => i.Qty * i.Price) };
        foreach (var i in items) tender.Items.Add(new TenderItem { Name = i.Name, Unit = i.Unit, Quantity = i.Qty, Specifications = $"{i.Name} as per the attached specification." });
        var round = new TenderRound { Tender = tender, RoundNumber = 1, Status = roundStatus, OpensAt = deadline.AddDays(-21), Deadline = deadline,
            ClosedAt = roundStatus == TenderRoundStatus.Open ? null : deadline.AddMinutes(5), CreatedByUserId = officer.Id, CreatedAt = deadline.AddDays(-22) };
        db.Tenders.Add(tender); db.TenderRounds.Add(round);
        await db.SaveChangesAsync();
        var bids = new Dictionary<Supplier, Bid>();
        foreach (var (supplier, factor, minutes, compliance) in offers)
        {
            var invitation = new TenderInvitation { TenderRoundId = round.Id, SupplierId = supplier.Id, Status = InvitationStatus.Accepted, InvitedByUserId = officer.Id, InvitedAt = deadline.AddDays(-20), RespondedAt = deadline.AddDays(-18) };
            var bid = new Bid { TenderInvitation = invitation, RevisionNumber = 1, Status = BidStatus.Submitted, CurrencyCode = "JOD", DeliveryDays = 21 + minutes % 9,
                PaymentTerms = "60 days after acceptance", CreatedAt = deadline.AddDays(-3), SubmittedAt = deadline.AddHours(-26).AddMinutes(minutes), ValidUntil = deadline.AddDays(90),
                ShippingCost = 0, ComplianceStatus = roundStatus == TenderRoundStatus.Open ? ComplianceStatus.NotChecked : compliance,
                ComplianceCheckedByUserId = roundStatus == TenderRoundStatus.Open ? null : officer.Id, ComplianceCheckedAt = roundStatus == TenderRoundStatus.Open ? null : deadline.AddDays(1),
                TechnicalProposal = "Fictional demonstration offer." };
            foreach (var (item, i) in tender.Items.Select((x, i) => (x, i)))
                bid.Items.Add(new BidItem { TenderItemId = item.Id, Quantity = item.Quantity, UnitPrice = decimal.Round(items[i].Price * factor, 2) });
            db.TenderInvitations.Add(invitation); db.Bids.Add(bid);
            bids[supplier] = bid;
        }
        foreach (var supplier in invitedOnly)
            db.TenderInvitations.Add(new TenderInvitation { TenderRoundId = round.Id, SupplierId = supplier.Id, Status = InvitationStatus.Pending, InvitedByUserId = officer.Id, InvitedAt = deadline.AddDays(-20) });
        await db.SaveChangesAsync();
        if (winner != null)
        {
            tender.AwardedBidId = bids[winner].Id; tender.AwardedAt = deadline.AddDays(24); tender.AwardedByUserId = officer.Id;
            tender.AwardApprovalEvidenceJson = "{\"note\":\"Fictional Orange Radar demonstration award.\"}";
            await db.SaveChangesAsync();
        }
        return (tender, round, bids);
    }
}
