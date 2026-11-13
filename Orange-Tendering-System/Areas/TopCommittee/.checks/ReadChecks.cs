using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
try
{
    var count = 0;
    void Check(bool result, string message) { if (!result) throw new Exception(message); count++; Console.WriteLine("PASS " + message); }
    var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer("Server=localhost;Database=UnusedOffline;Trusted_Connection=True").Options;
    using var original = new ApplicationDbContext(options);
    using var compatible = new ApplicationDbContext(TopCommitteeCompatibility.Options(options));
    var converter = compatible.Model.FindEntityType(typeof(CommitteeRound))!.FindProperty(nameof(CommitteeRound.CommitteeType))!.GetTypeMapping().Converter!;
    var originalConverter = original.Model.FindEntityType(typeof(CommitteeRound))!.FindProperty(nameof(CommitteeRound.CommitteeType))!.GetTypeMapping().Converter!;
    Check((CommitteeType)converter.ConvertFromProvider("TopCommittee")! == CommitteeType.Top, "Legacy committee values read as Top");
    Check((string)converter.ConvertToProvider(CommitteeType.Top)! == "Top", "Canonical committee values still write as Top");
    Check((string)originalConverter.ConvertToProvider(TopCommitteeCompatibility.LegacyTop)! != "TopCommittee" && !ReferenceEquals(original.Model, compatible.Model), "Shared EF model is not modified or reused as the compatibility model");
    var reportConverter = compatible.Model.FindEntityType(typeof(TenderReport))!.FindProperty(nameof(TenderReport.ReportType))!.GetTypeMapping().Converter!;
    Check((ReportType)reportConverter.ConvertFromProvider("UnifiedEvaluation")! == ReportType.FullEvaluation, "UnifiedEvaluation remains an evaluation report, not an executive summary");
    var statusConverter = compatible.Model.FindEntityType(typeof(Tender))!.FindProperty(nameof(Tender.Status))!.GetTypeMapping().Converter!;
    Check((TenderStatus)statusConverter.ConvertFromProvider("Awaiting Committee Decision")! == TenderStatus.TopCommitteeReview, "Legacy tender stage can be read");
    var query = new TopCommitteeService(compatible, null!).AssignedRounds("assigned-member").ToQueryString();
    Check(query.Contains("TopCommittee") && query.Contains("assigned-member") && query.Contains("CommitteeMembers"), "SQL includes legacy membership and round values while retaining user scoping");
    var incomplete = new RoundDetails { Round = new() { TenderRound = new() { Tender = new() { Status = TenderStatus.TopCommitteeReview } }, TenderReport = new() { ReportType = ReportType.FullEvaluation, SubmittedAt = DateTime.UtcNow, Status = ReportStatus.Submitted } }, IsLatest = true };
    Check(TopCommitteePolicy.Blockers(incomplete).Count > 0, "Legacy display does not bypass voting or AI eligibility");
    if (args.Contains("--live"))
    {
        var config = new ConfigurationBuilder().SetBasePath(args[0]).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).AddUserSecrets(typeof(ApplicationUser).Assembly, true).AddEnvironmentVariables().Build();
        var services = new ServiceCollection(); services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(config.GetConnectionString("Default Connection")));
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var liveOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(config.GetConnectionString("Default Connection")).Options;
        using var live = new ApplicationDbContext(TopCommitteeCompatibility.Options(liveOptions));
        var service = new TopCommitteeService(live, manager);
        var report = await new TopCommitteeReportingService(live).Read();
        Check(report.Requests == await live.PurchaseRequests.CountAsync() && report.Tenders == await live.Tenders.CountAsync(), "LIVE entity totals match SQL");
        Check(report.RequestStatuses.Sum(p => p.Count) == report.Requests && report.TenderStatuses.Sum(p => p.Count) == report.Tenders, "LIVE status totals reconcile");
        Check(report.Months.Count == 6 && report.SupplierStatuses.Single(p => p.Label == "Registered").Count == report.Suppliers, "LIVE monthly series and supplier registration semantics");
        foreach (var email in new[] { "top.demo@orange.test", "topcommittee@orange.com" })
        {
            var user = await manager.FindByEmailAsync(email) ?? throw new Exception("Account missing");
            var rounds = await service.RoundList(user.Id).AsNoTracking().AsSplitQuery().ToListAsync();
            foreach (var round in rounds) Check(await service.Load(user.Id, round.Id) is not null, "LIVE assigned round materializes");
        }
        Check(!live.ChangeTracker.HasChanges(), "LIVE compatibility reads do not mark stored records for updates");
    }
    Console.WriteLine($"Completed {count} checks. Database operations, when enabled, were read-only.");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
