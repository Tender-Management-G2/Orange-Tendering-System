using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed class SourcingManagerClarificationsIndexViewModel
{
    public string? Search { get; init; }
    public string? Status { get; init; }
    public IReadOnlyList<SourcingManagerClarificationRow> Threads { get; init; } = [];
}

public sealed record SourcingManagerClarificationRow(
    int Id,
    string Subject,
    string OtherParticipant,
    string TenderTitle,
    ClarificationStatus Status,
    DateTime CreatedAt,
    DateTime? LastMessageAt);

public sealed class SourcingManagerClarificationCreateViewModel
{
    public int? TenderId { get; init; }
    public IReadOnlyList<SourcingManagerTenderOption> Tenders { get; init; } = [];
    public IReadOnlyList<SourcingManagerRecipientOption> Recipients { get; init; } = [];
}

public sealed record SourcingManagerTenderOption(int Id, string Title);
public sealed record SourcingManagerRecipientOption(string UserId, string DisplayName, string Role);

public sealed class SourcingManagerClarificationDetailsViewModel
{
    public int Id { get; init; }
    public string Subject { get; init; } = string.Empty;
    public string TenderTitle { get; init; } = string.Empty;
    public string OtherParticipant { get; init; } = string.Empty;
    public ClarificationStatus Status { get; init; }
    public bool CanReply { get; init; }
    public bool CanClose { get; init; }
    public IReadOnlyList<SourcingManagerClarificationMessageRow> Messages { get; init; } = [];
}

public sealed record SourcingManagerClarificationMessageRow(string Sender, string Message, DateTime SentAt);
