using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Reports;

public class ReportSigningOptions
{
    public bool RequireCertificateBacked { get; set; }
    public Dictionary<string, string> UserCertificateThumbprints { get; set; } = [];
    public string StoreLocation { get; set; } = "CurrentUser";
}
public class ReportRevisionPolicy
{
    public string? Version { get; set; }
    public List<RevisionRoute> Routes { get; set; } = [];
}
public class RevisionRoute
{
    public ReportReviewStage Source { get; set; }
    public RevisionTarget Target { get; set; }
    public string[] ReturningUserIds { get; set; } = [];
}
public class ReportPdfOptions { public string? FontDirectory { get; set; } }
public record ReportSeat(int MemberId, string UserId, string Name, string Seat);
public record ReportBid(int Id, string Supplier, int SupplierId, string Currency, decimal Total,
    string Technical, int? DeliveryDays, string Payment, string Compliance, string Notes, string Missing,
    string[][] Items, string[] Documents, string[][] AdditionalCosts, int Revision, string SubmittedUtc, string ValidUntilUtc);
public record ReportMemberScore(int BidId, int MemberId, decimal Financial, decimal Technical, decimal Weighted, string Observations);
public record ReportInputs(int ReportId, int Version, int TenderId, string Title, string Description, int PurchaseRequestId,
    string CreatedUtc, string Requirements, string[][] Items, int WeightVersion, decimal FinancialWeight, decimal TechnicalWeight,
    string WeightApprover, string WeightApprovedUtc, List<ReportSeat> Committee, List<ReportBid> Bids,
    List<ReportMemberScore> Scores, IReadOnlyList<RankedBid> Ranking, string AiModel, string AiFindings, string AiEvidence,
    string Explanation, string ContentHash);
public record FrozenReport(ReportInputs Inputs, string[][] ApprovalHistory);
public record RenderedReport(byte[] Bytes, int SignaturePageIndex);
