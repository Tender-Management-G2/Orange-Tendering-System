using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.ViewModels;

// Only these two fields may be changed by the supplier.
public sealed class SupplierContactInputModel
{
    [StringLength(40)]
    public string? Phone { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }
}
