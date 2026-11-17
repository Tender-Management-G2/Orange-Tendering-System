using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

// Sourcing Officer dashboard. Every value is read from SQL Server by the officer DashboardController; nothing is invented.
public sealed class SourcingOfficerDashboardViewModel
{
    public string? DisplayName { get; init; }

    // KPI cards
    public int ActionItems { get; init; }
    public int ActiveTenders { get; init; }
    public int InvitedSuppliers { get; init; }
    public int OngoingEvaluations { get; init; }

    // "Assigned by Sourcing Manager": real Manager -> Officer handoffs that still need the Officer.
    public IReadOnlyList<OfficerHandoff> Handoffs { get; init; } = [];

    // Tender Workflow card: the most recent active tender, if any.
    public OfficerTenderSummary? FocusTender { get; init; }

    public IReadOnlyList<OfficerTenderSummary> ActiveTenderList { get; init; } = [];
    public IReadOnlyList<OfficerStatusSlice> StatusSlices { get; init; } = [];
    public IReadOnlyList<OfficerActivity> Activities { get; init; } = [];
}

public enum OfficerHandoffKind { Retender, ProceedApproved, FinalDecision }

public sealed record OfficerHandoff(OfficerHandoffKind Kind, int TenderId, string TenderTitle, string? Reason, int RoundNumber, int MaxRounds, DateTime At);

public sealed record OfficerTenderSummary(int Id, string Title, TenderStatus Status, int RoundNumber, DateTime? Deadline, bool Retendering);

// Key: bidding | evaluation | awarded | retendering
public sealed record OfficerStatusSlice(string Key, int Count);

public sealed record OfficerActivity(string Title, string Message, DateTime At, string? Link);
