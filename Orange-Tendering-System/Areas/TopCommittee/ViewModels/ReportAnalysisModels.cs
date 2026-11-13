using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.Areas.TopCommittee.ViewModels;

public sealed class ReportAnalysisInput
{
    [Range(1, int.MaxValue)] public int RoundId { get; set; }
    [StringLength(2000)] public string? Question { get; set; }
}

public sealed record ReportAnalysis(string Answer, string Model, int ReportId, int ReportVersion,
    DateTime GeneratedAt, bool Truncated);

public sealed class ReportAnalysisException(string message) : Exception(message);
