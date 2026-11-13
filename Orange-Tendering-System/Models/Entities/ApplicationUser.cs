using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Orange_Tendering_System.Models.Entities
{
    // Extends the Identity user. Internal employees have a DepartmentId; supplier accounts have a SupplierId.
    public class ApplicationUser : IdentityUser
    {
        [MaxLength(150)]
        public string FullName { get; set; } = string.Empty;

        public int? DepartmentId { get; set; }
        public Department? Department { get; set; }

        public int? SupplierId { get; set; }
        public Supplier? Supplier { get; set; }

        public bool IsActive { get; set; } = true;

        // Admin-provisioned employees cannot enter a workspace until this is cleared.
        public bool MustChangePassword { get; set; }

        // Supplier email OTP state; the code itself is generated and validated by Identity.
        public DateTime? EmailOtpSentAt { get; set; }
        public int EmailOtpFailedAttempts { get; set; }
    }
}
