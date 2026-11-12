using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // The supplier company. Login accounts live in AspNetUsers (ApplicationUser.SupplierId).
    [Index(nameof(RegistrationNumber), IsUnique = true)]
    public class Supplier
    {
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, MaxLength(80)]
        public string RegistrationNumber { get; set; } = string.Empty;

        [MaxLength(80)]
        public string? TaxNumber { get; set; }

        [Required, MaxLength(256), EmailAddress]
        public string Email { get; set; } = string.Empty;

        [MaxLength(40)]
        public string? Phone { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        public SupplierStatus Status { get; set; } = SupplierStatus.Pending;

        // Admin review of the registration (approval, rejection or suspension reason).
        [MaxLength(1000)]
        public string? ReviewNotes { get; set; }
        public string? ReviewedByUserId { get; set; }
        public ApplicationUser? ReviewedByUser { get; set; }
        public DateTime? ReviewedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public ICollection<Document> Documents { get; set; } = new List<Document>();
        public ICollection<TenderInvitation> Invitations { get; set; } = new List<TenderInvitation>();
    }
}
