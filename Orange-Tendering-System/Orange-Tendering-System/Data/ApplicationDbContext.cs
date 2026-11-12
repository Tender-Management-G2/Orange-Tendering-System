using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        // Organization
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Document> Documents => Set<Document>();

        // Purchase requests
        public DbSet<PurchaseRequest> PurchaseRequests => Set<PurchaseRequest>();
        public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();
        public DbSet<PurchaseRequestReview> PurchaseRequestReviews => Set<PurchaseRequestReview>();

        // Tenders and bids
        public DbSet<Tender> Tenders => Set<Tender>();
        public DbSet<TenderRound> TenderRounds => Set<TenderRound>();
        public DbSet<TenderItem> TenderItems => Set<TenderItem>();
        public DbSet<TenderInvitation> TenderInvitations => Set<TenderInvitation>();
        public DbSet<Bid> Bids => Set<Bid>();
        public DbSet<BidItem> BidItems => Set<BidItem>();

        // Committees, evaluation and reports
        public DbSet<CommitteeMember> CommitteeMembers => Set<CommitteeMember>();
        public DbSet<CommitteeRound> CommitteeRounds => Set<CommitteeRound>();
        public DbSet<EvaluationCriterion> EvaluationCriteria => Set<EvaluationCriterion>();
        public DbSet<EvaluationScore> EvaluationScores => Set<EvaluationScore>();
        public DbSet<CommitteeVote> CommitteeVotes => Set<CommitteeVote>();
        public DbSet<TenderReport> TenderReports => Set<TenderReport>();

        // Communication and auditing
        public DbSet<Clarification> Clarifications => Set<Clarification>();
        public DbSet<ClarificationMessage> ClarificationMessages => Set<ClarificationMessage>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            // Store every enum as readable text (e.g. "Submitted") instead of a number.
            configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(30);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Supplier and ApplicationUser are linked twice (account's company, and the Admin who reviewed it),
            // so EF needs to be told these are two separate relationships.
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.Supplier).WithMany().HasForeignKey(u => u.SupplierId);
            builder.Entity<Supplier>()
                .HasOne(s => s.ReviewedByUser).WithMany().HasForeignKey(s => s.ReviewedByUserId);

            // Protect history: deleting a record never cascades into other domain records.
            // (Identity's own tables keep their default cascade behavior.)
            var domainForeignKeys = builder.Model.GetEntityTypes()
                .Where(e => e.ClrType.Namespace == typeof(Tender).Namespace)
                .SelectMany(e => e.GetForeignKeys());

            foreach (var foreignKey in domainForeignKeys)
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }
    }
}
