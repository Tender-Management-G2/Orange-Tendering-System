using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

public sealed class BidBankGuarantee
{
    [Key] public int BidId { get; set; }
    public Bid Bid { get; set; } = null!;
    [MaxLength(40)] public string? Issuer { get; set; }
    [MaxLength(40)] public string? Number { get; set; }
    [Precision(19, 3)] public decimal? Amount { get; set; }
    [MaxLength(3)] public string? Currency { get; set; }
    public DateOnly? ExpiresOn { get; set; }
}
