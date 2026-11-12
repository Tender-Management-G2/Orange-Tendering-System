using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.Models.Entities
{
    // Created by a department employee and reviewed directly by the Sourcing Officer.
    public class PurchaseRequest
    {
        public int Id { get; set; }

        public int DepartmentId { get; set; }
        public Department Department { get; set; } = null!;

        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser CreatedByUser { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Justification { get; set; } = string.Empty;

        public RequestPriority Priority { get; set; } = RequestPriority.Normal;

        public DateTime? NeededBy { get; set; }

        public PurchaseRequestStatus Status { get; set; } = PurchaseRequestStatus.Draft;

        public DateTime CreatedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }

        public ICollection<PurchaseRequestItem> Items { get; set; } = new List<PurchaseRequestItem>();
        public ICollection<PurchaseRequestReview> Reviews { get; set; } = new List<PurchaseRequestReview>();
        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<Tender> Tenders { get; set; } = new List<Tender>();
    }
}
