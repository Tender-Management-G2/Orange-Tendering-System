using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

public enum RoundDecisionKind { Retender, ProceedException, Proceed }
// Recorded: an officer decision that needs no second approval (re-tender).
// Withdrawn: a pending proceed request closed because the officer re-tendered the round instead.
public enum RoundDecisionStatus { Pending, Approved, Rejected, Recorded, Withdrawn }
public enum ExternalInvitationStatus { Queued, Sent, Failed, ChannelNotConfigured }

// Documented Sourcing Officer decision for one tendering round: either a re-tender, or a request to proceed
// with fewer suppliers than the normal minimum. A proceed request only relaxes the offer-count rule after the
// assigned Sourcing Manager approves it; compliance, evaluation, approvals and award rules are unaffected.
[Index(nameof(TenderRoundId), nameof(Kind))]
public class RoundDecision
{
    public int Id { get; set; }
    public int TenderRoundId { get; set; }
    public TenderRound TenderRound { get; set; } = null!;
    public RoundDecisionKind Kind { get; set; }
    public RoundDecisionStatus Status { get; set; }
    // Distinct suppliers with a submitted offer in the round when the decision was recorded.
    public int OfferCount { get; set; }
    [Required, MaxLength(2000)] public string Justification { get; set; } = "";
    public string RequestedByUserId { get; set; } = "";
    public ApplicationUser RequestedByUser { get; set; } = null!;
    public DateTime RequestedAt { get; set; }
    public string? DecidedByUserId { get; set; }
    public ApplicationUser? DecidedByUser { get; set; }
    public DateTime? DecidedAt { get; set; }
    [MaxLength(2000)] public string? DecisionComment { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

// Invitation of a company that has no supplier registration yet. It is delivered through the configured
// external channel and never creates a supplier, an account, a TenderInvitation or a bid.
[Index(nameof(TenderRoundId), nameof(Email), IsUnique = true)]
public class ExternalSupplierInvitation
{
    public int Id { get; set; }
    public int TenderRoundId { get; set; }
    public TenderRound TenderRound { get; set; } = null!;
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    [Required, MaxLength(200)] public string CompanyName { get; set; } = "";
    [Required, MaxLength(256)] public string Email { get; set; } = "";
    [MaxLength(40)] public string? Phone { get; set; }
    [Required, MaxLength(30)] public string Channel { get; set; } = "Email";
    public ExternalInvitationStatus Status { get; set; }
    public int Attempts { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public ApplicationUser CreatedByUser { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}
