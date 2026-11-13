

// sara rjoub 


using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels
{
    public class SupplierDocumentReviewViewModel
    {
        [Range(1, int.MaxValue)]
        public int SupplierId { get; set; }

        [Range(1, int.MaxValue)]
        public int DocumentId { get; set; }

        [Required]
        public DocumentVerificationStatus Status { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
