using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // The official line items suppliers price (based on the purchase request items).
    public class TenderItem
    {
        public int Id { get; set; }

        public int TenderId { get; set; }
        public Tender Tender { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Specifications { get; set; }

        // Copied from the purchase request item; null for tenders created before categories existed.
        public int? CategoryId { get; set; }
        public ProcurementCategory? Category { get; set; }

        [Precision(18, 3)]
        public decimal Quantity { get; set; }

        [Required, MaxLength(30)]
        public string Unit { get; set; } = string.Empty;
    }
}
