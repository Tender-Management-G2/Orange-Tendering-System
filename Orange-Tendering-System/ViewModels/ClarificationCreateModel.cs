using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.ViewModels;

// People for the "Create" clarification form (Views/Clarifications/_ClarificationCreateForm.cshtml):
// Options = people you share a request, tender or bid with; Pending = people in allowed roles without a shared record yet.
public sealed record ClarificationCreateModel(IReadOnlyList<ClarificationOption> Options, IReadOnlyList<ClarificationService.PendingContact> Pending);
