using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

public enum PreTenderReviewStatus { Pending, Approved, Returned }
public enum PreTenderDecision { Approve, Return }

// Top Committee review of the PREPARED tender before it is published to suppliers. It is never a supplier award:
// the final award is a separate vote on the signed evaluation report. A return creates a new attempt; old attempts stay.
[Index(nameof(TenderId), nameof(Attempt), IsUnique = true)]
public class PreTenderReview
{
    public int Id { get; set; }
    public int TenderId { get; set; }
    public Tender Tender { get; set; } = null!;
    public int Attempt { get; set; } = 1;
    public PreTenderReviewStatus Status { get; set; } = PreTenderReviewStatus.Pending;
    // The threshold in force when this attempt was submitted (configuration, frozen per attempt).
    public int ApprovalsRequired { get; set; }
    [Required] public string SubmittedByUserId { get; set; } = "";
    public DateTime SubmittedAt { get; set; }
    [MaxLength(2000)] public string? OfficerNote { get; set; }
    public DateTime? CompletedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<PreTenderVote> Votes { get; set; } = new List<PreTenderVote>();
}

[Index(nameof(PreTenderReviewId), nameof(CommitteeMemberId), IsUnique = true)]
public class PreTenderVote
{
    public int Id { get; set; }
    public int PreTenderReviewId { get; set; }
    public PreTenderReview PreTenderReview { get; set; } = null!;
    public int CommitteeMemberId { get; set; }
    public CommitteeMember CommitteeMember { get; set; } = null!;
    [Required, MaxLength(450)] public string UserId { get; set; } = "";
    public PreTenderDecision Decision { get; set; }
    [MaxLength(2000)] public string? Comment { get; set; }
    public DateTime VotedAt { get; set; }
}
