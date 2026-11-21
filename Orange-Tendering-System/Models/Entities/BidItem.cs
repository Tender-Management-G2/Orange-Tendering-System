using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // Price for one tender item in one bid version. Line total = Quantity × UnitPrice (calculated).
    [Index(nameof(BidId), nameof(TenderItemId), IsUnique = true)]
    public class BidItem
    {
        public int Id { get; set; }

        public int BidId { get; set; }
        public Bid Bid { get; set; } = null!;

        public int TenderItemId { get; set; }
        public TenderItem TenderItem { get; set; } = null!;

        [Precision(18, 3)]
        public decimal Quantity { get; set; }

        // Unit price excluding tax.
        [Precision(19, 3)]
        public decimal UnitPrice { get; set; }

        // Offered product or service, brand and model where relevant.
        [MaxLength(300)]
        public string? OfferedProduct { get; set; }

        public string? OfferedSpecifications { get; set; }

        // Item warranty, notes or deviations from the requested specification.
        [MaxLength(2000)]
        public string? Notes { get; set; }
    }
}
