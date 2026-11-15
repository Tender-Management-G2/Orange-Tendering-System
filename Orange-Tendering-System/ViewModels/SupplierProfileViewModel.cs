namespace Orange_Tendering_System.ViewModels;

public sealed class SupplierProfileViewModel
{
    public List<SupplierDocumentViewModel> Documents { get; set; } = [];
    public SupplierContactInputModel Contact { get; init; } = new();
    public string? AccessMessage { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public string? TaxNumber { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? ReviewedAt { get; init; }
}
