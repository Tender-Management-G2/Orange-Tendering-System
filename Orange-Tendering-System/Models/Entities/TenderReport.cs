using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // A report version. Once submitted it is not overwritten: a correction creates a new row
    // with VersionNumber + 1 and PreviousReportId pointing to the old version.
    // The receiver's decision (e.g. the Sourcing Manager returning, rejecting or accepting/forwarding) is stored on the version it applies to.
    [Index(nameof(TenderRoundId), nameof(ReportType), nameof(VersionNumber), IsUnique = true)]
    public class TenderReport
    {
        public int Id { get; set; }

        public int TenderRoundId { get; set; }
        public TenderRound TenderRound { get; set; } = null!;

        public ReportType ReportType { get; set; }

        public int VersionNumber { get; set; } = 1;

        public int? PreviousReportId { get; set; }
        public TenderReport? PreviousReport { get; set; }

        [Required]
        public string Content { get; set; } = string.Empty;

        // System recommendation (not the award, which is Tender.AwardedBidId).
        public int? RecommendedBidId { get; set; }
        public Bid? RecommendedBid { get; set; }

        // Frozen copy of the calculated ranking shown in this report version.
        public string? RankingSnapshotJson { get; set; }

        public ReportStatus Status { get; set; } = ReportStatus.Draft;

        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser CreatedByUser { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }

        public string? ReviewedByUserId { get; set; }
        public ApplicationUser? ReviewedByUser { get; set; }

        [MaxLength(2000)]
        public string? ReviewComment { get; set; }
        public DateTime? ReviewedAt { get; set; }

        // Immutable canonical report. Personalized access copies never replace these bytes.
        public byte[]? PdfContent { get; set; }
        [MaxLength(64)] public string? PdfSha256 { get; set; }
        public string? SnapshotJson { get; set; }
        [MaxLength(64)] public string? SnapshotSha256 { get; set; }
        [MaxLength(64)] public string? SourceSha256 { get; set; }
        [MaxLength(40)] public string? TemplateVersion { get; set; }
        public int? SignaturePageIndex { get; set; }
        public DateTime? SupersededAt { get; set; }
        [Timestamp] public byte[] RowVersion { get; set; } = [];

        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
