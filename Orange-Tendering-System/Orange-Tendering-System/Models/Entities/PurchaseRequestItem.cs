using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    public class PurchaseRequestItem
    {
        public int Id { get; set; }

        public int PurchaseRequestId { get; set; }
        public PurchaseRequest PurchaseRequest { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Specifications { get; set; }

        [Precision(18, 3)]
        public decimal Quantity { get; set; }

        [Required, MaxLength(30)]
        public string Unit { get; set; } = string.Empty;
    }
}
