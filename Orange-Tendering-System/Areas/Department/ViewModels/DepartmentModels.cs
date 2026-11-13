using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.Department.ViewModels;

public class RequestForm
{
    public int Id { get; set; }
    public string? Version { get; set; }
    [Required, StringLength(200)] public string Title { get; set; } = "";
    [Required, StringLength(10000)] public string Justification { get; set; } = "";
    [EnumDataType(typeof(RequestPriority))] public RequestPriority Priority { get; set; } = RequestPriority.Normal;
    [Required, DataType(DataType.Date)] public DateTime? NeededBy { get; set; }
    public List<RequestItemForm> Items { get; set; } = [new()];
    public List<IFormFile> Attachments { get; set; } = [];
    [BindNever] public string? ReturnComment { get; set; }
    [BindNever] public List<Document> ExistingDocuments { get; set; } = [];
}

public class RequestItemForm
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [StringLength(10000)] public string? Specifications { get; set; }
    [Range(typeof(decimal), "0.001", "999999999999999.999")]
    public decimal Quantity { get; set; } = 1;
    [Required, StringLength(30)] public string Unit { get; set; } = "";
}

public record RequestSnapshot(string Title, string Justification, RequestPriority Priority,
    DateTime? NeededBy, List<RequestItemForm> Items, List<string>? Attachments = null);

public class RequestList
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public RequestPriority? Priority { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / 20m));
    public List<PurchaseRequest> Requests { get; set; } = [];
    public string UserId { get; set; } = "";
}

public record RequestTable(List<PurchaseRequest> Requests, string UserId);

public class RequestDetails
{
    public PurchaseRequest Request { get; set; } = null!;
    public bool CanEdit { get; set; }
    public List<Clarification> Clarifications { get; set; } = [];
}

public class DepartmentDashboard
{
    public string DepartmentName { get; set; } = "";
    public Dictionary<string, int> Counts { get; set; } = [];
    public List<PurchaseRequest> Recent { get; set; } = [];
    public List<PurchaseRequest> ActionRequired { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<PurchaseRequestReview> RecentActivity { get; set; } = [];
    public string UserId { get; set; } = "";

    // Adapt Department data to the existing project's dashboard presentation components.
    public Orange_Tendering_System.ViewModels.WorkspaceDashboardViewModel Presentation => new()
    {
        DataConnected = true,
        DisplayName = DepartmentName,
        Metrics = new Dictionary<string, int>
        {
            ["purchase-requests"] = Counts.Values.Sum(),
            ["awaiting-review"] = Counts.GetValueOrDefault("Submitted") + Counts.GetValueOrDefault("Under Review"),
            ["returned"] = Counts.GetValueOrDefault("Returned"),
            ["accepted"] = Counts.GetValueOrDefault("Accepted")
        },
        Statuses = RequestDisplay.Statuses.Select(status =>
            new Orange_Tendering_System.ViewModels.DashboardStatus(status, Counts.GetValueOrDefault(status), "")).ToList(),
        Tasks = ActionRequired.Select(request => new Orange_Tendering_System.ViewModels.DashboardTask(
            request.Title, $"PR-{request.Id}", RequestDisplay.Status(request))).ToList(),
        Activities = RecentActivity.Take(4).Select(review =>
            new Orange_Tendering_System.ViewModels.DashboardActivity(
                $"PR-{review.PurchaseRequestId} · {review.Decision}",
                review.PurchaseRequest.Title, RequestDisplay.Date(review.ReviewedAt ?? review.SubmittedAt))).ToList()
    };
}

public static class RequestDisplay
{
    public static readonly string[] Statuses = ["Draft", "Submitted", "Under Review", "Accepted", "Rejected", "Returned", "Converted to Tender"];

    public static string Status(PurchaseRequest request) => request.Tenders.Count > 0
        ? "Converted to Tender" : request.Status switch
        {
            PurchaseRequestStatus.Approved => "Accepted",
            PurchaseRequestStatus.UnderReview => "Under Review",
            PurchaseRequestStatus.ConvertedToTender => "Converted to Tender",
            _ => request.Status.ToString()
        };

    public static bool Editable(PurchaseRequest request, string userId) =>
        request.CreatedByUserId == userId && request.Tenders.Count == 0 &&
        request.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned;

    public static string Date(DateTime? date) => date?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "—";
}
