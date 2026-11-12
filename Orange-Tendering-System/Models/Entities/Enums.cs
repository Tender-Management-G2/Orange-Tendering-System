namespace Orange_Tendering_System.Models.Entities
{
    // All enums are stored as readable strings in SQL Server (see ApplicationDbContext.ConfigureConventions).

    public enum SupplierStatus { Pending, Approved, Rejected, Suspended }

    public enum DocumentVerificationStatus { Pending, Verified, Rejected }

    public enum RequestPriority { Low, Normal, High, Urgent }

    public enum PurchaseRequestStatus { Draft, Submitted, Returned, Rejected, Accepted }

    public enum ReviewDecision { Pending, Accepted, Returned, Rejected }

    public enum TenderStatus { Draft, Bidding, Evaluation, OfficerReview, ManagerReview, TopCommitteeReview, Awarded, Rejected, Cancelled }

    public enum TenderRoundStatus { Draft, Open, Closed, Failed, SentToEvaluation }

    public enum InvitationStatus { Pending, Accepted, Declined }

    public enum BidStatus { Draft, Submitted, Superseded, Withdrawn }

    public enum ComplianceStatus { NotChecked, Compliant, NonCompliant, PendingClarification }

    public enum CommitteeType { Evaluation, Top }

    public enum CommitteeRoundStatus { InProgress, Approved, Rejected }

    public enum CriterionType { Technical, Financial }

    public enum VoteDecision { Approve, Reject }

    public enum ReportType { OfficerComparison, FullEvaluation, ExecutiveSummary }

    public enum ReportStatus { Draft, Submitted, Returned, Rejected, Accepted }

    public enum ClarificationStatus { Open, Answered, Closed }
}
