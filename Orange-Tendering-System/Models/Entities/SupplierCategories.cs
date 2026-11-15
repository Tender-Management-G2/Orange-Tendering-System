namespace Orange_Tendering_System.Models.Entities;

/// <summary>Categories suppliers choose when registering, used to filter sourcing invitations.</summary>
public static class SupplierCategories
{
    public const string InformationTechnology = "IT & Electronics";
    public const string OfficeSupplies = "Office Supplies";
    public const string Furniture = "Furniture & Fixtures";
    public const string Construction = "Construction & Maintenance";
    public const string Transport = "Transport & Logistics";
    public const string ProfessionalServices = "Professional Services";
    public const string Medical = "Medical & Safety";
    public const string Food = "Food & Hospitality";
    public const string Facilities = "Cleaning & Facilities";
    public const string Telecommunications = "Telecommunications";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All =
    [
        InformationTechnology, OfficeSupplies, Furniture, Construction, Transport,
        ProfessionalServices, Medical, Food, Facilities, Telecommunications, Other
    ];
}
