using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities;

// Master data for what a department requests (and what tender items are). Managed by Admin; never deleted once used,
// only deactivated, so historical request and tender items keep their category.
[Index(nameof(Name), IsUnique = true)]
public class ProcurementCategory
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(300)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

// Units offered in the request form. Kept as a short fixed list: the unit is stored as text on each item.
public static class UnitsOfMeasure
{
    public static readonly IReadOnlyList<string> All =
    [
        "Piece", "Unit", "Set", "Box", "Pack", "Pair", "Kilogram", "Ton", "Liter", "Meter", "Square meter", "License"
    ];
}
