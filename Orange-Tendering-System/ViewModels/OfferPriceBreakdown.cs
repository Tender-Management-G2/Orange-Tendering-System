using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels;

public sealed record OfferPriceBreakdown(string Currency, decimal Products, decimal Shipping, decimal Installation,
    decimal Maintenance, decimal Other, decimal Tax, bool Live = false)
{
    public decimal Services => Shipping + Installation + Maintenance + Other;
    public decimal Subtotal => Products + Services;
    public decimal Total => Subtotal + Tax;
    public bool ShowStandardTaxRate => Live || Tax == Bid.CalculateSalesTax(Subtotal);

    public static OfferPriceBreakdown FromBid(Bid bid) => new(bid.CurrencyCode, bid.ProductsSubtotal,
        bid.ShippingCost, bid.InstallationCost, bid.MaintenanceCost, bid.OtherCost, bid.TaxAmount);
}
