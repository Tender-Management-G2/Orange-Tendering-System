using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

// Separate EF model configuration used only by TopCommittee. No additional DbContext type or schema.
// It never turns an evaluation report into an ExecutiveSummary or relaxes the approval policy.
public static class TopCommitteeCompatibility
{
    public const CommitteeType LegacyTop = (CommitteeType)1001;

    public static DbContextOptions<ApplicationDbContext> Options(DbContextOptions<ApplicationDbContext> original) =>
        new DbContextOptionsBuilder<ApplicationDbContext>(original)
            .ReplaceService<IModelCustomizer, TopCommitteeModelCustomizer>().Options;

    internal static void Configure(ModelBuilder builder)
    {
        var committee = new ValueConverter<CommitteeType, string>(
            value => CommitteeStorage(value), value => ReadCommittee(value));
        builder.Entity<CommitteeMember>().Property(m => m.CommitteeType).HasConversion(committee);
        builder.Entity<CommitteeRound>().Property(r => r.CommitteeType).HasConversion(committee);
        builder.Entity<Tender>().Property(t => t.Status).HasConversion(new ValueConverter<TenderStatus, string>(
            value => value.ToString(), value => ReadTenderStatus(value)));
        builder.Entity<TenderReport>().Property(r => r.ReportType).HasConversion(new ValueConverter<ReportType, string>(
            value => value.ToString(), value => ReadReportType(value)));
    }

    public static string CommitteeStorage(CommitteeType value) => value == LegacyTop ? "TopCommittee" : value.ToString();
    public static CommitteeType ReadCommittee(string value) => value == "TopCommittee" ? CommitteeType.Top : Enum.Parse<CommitteeType>(value);
    public static TenderStatus ReadTenderStatus(string value) => value == "Awaiting Committee Decision"
        ? TenderStatus.TopCommitteeReview : Enum.Parse<TenderStatus>(value);
    public static ReportType ReadReportType(string value) => value == "UnifiedEvaluation"
        ? ReportType.FullEvaluation : Enum.Parse<ReportType>(value);
}

// The replacement gets its own EF internal service provider/model cache; the shared model is untouched.
internal sealed class TopCommitteeModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        TopCommitteeCompatibility.Configure(modelBuilder);
    }
}
