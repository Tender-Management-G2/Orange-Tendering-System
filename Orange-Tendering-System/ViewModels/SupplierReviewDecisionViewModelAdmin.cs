// sara rjoub 



using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels
{
    public class SupplierReviewDecisionViewModel
    {
        [Range(1, int.MaxValue)]
        public int SupplierId { get; set; }

        [Required]
        public SupplierStatus Decision { get; set; }

        [StringLength(1000)]
        public string? ReviewNotes { get; set; }
    }
}
