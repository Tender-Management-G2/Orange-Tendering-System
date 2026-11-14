// sara rjoub 



namespace Orange_Tendering_System.ViewModels
{
    public class SupplierDetailsViewModel
    {
        public int Id { get; set; }

        public string CompanyName { get; set; } = "";
        public string RegistrationNumber { get; set; } = "";
        public string? TaxNumber { get; set; }

        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string? Address { get; set; }

        public string Status { get; set; } = "";
        public string? ReviewNotes { get; set; }
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }

        public List<SupplierAccountViewModel> Accounts
        { get; set; } = new();

        public List<AdminSupplierDocumentViewModel> Documents
        { get; set; } = new();

        public List<SupplierInvitationViewModel> Invitations
        { get; set; } = new();
    }

    public class SupplierAccountViewModel
    {
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsActive { get; set; }
    }

    public class AdminSupplierDocumentViewModel
    {
        public int Id { get; set; }
        public string FileName { get; set; } = "";
        public string? DocumentType { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string VerificationStatus { get; set; } = "";
        public string? VerificationNotes { get; set; }
    }

    public class SupplierInvitationViewModel
    {
        public int TenderId { get; set; }
        public string TenderTitle { get; set; } = "";
        public int RoundNumber { get; set; }
        public DateTime InvitedAt { get; set; }
        public string Status { get; set; } = "";
        public bool OfferSubmitted { get; set; }
    }
}
