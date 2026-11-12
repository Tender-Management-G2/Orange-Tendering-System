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

        [Precision(18, 2)]
        public decimal UnitPrice { get; set; }

        public string? OfferedSpecifications { get; set; }
    }
}
