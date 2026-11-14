using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.ViewModels;

public sealed class SupplierClarificationInputModel
{
    public int? BidId { get; set; }
    public int? InvitationId { get; set; }
    [Required, StringLength(200)]
    public string Subject { get; set; } = "";
    [Required, StringLength(10000)]
    public string Message { get; set; } = "";
}

public sealed class SupplierClarificationViewModel
{
    public int Id { get; set; }
    public string Subject { get; set; } = "";
    public string Status { get; set; } = "";
    public bool CanReply { get; set; }
    public List<SupplierMessageViewModel> Messages { get; set; } = new();
}

public sealed record SupplierMessageViewModel(int Id, string Sender, string Message, DateTime At, List<SupplierDocumentViewModel> Documents);
public sealed record SupplierNotificationViewModel(int Id, string Title, string Message, DateTime At, bool IsRead, string? Link);
