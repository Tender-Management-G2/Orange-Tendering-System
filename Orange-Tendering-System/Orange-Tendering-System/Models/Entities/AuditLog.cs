using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // Who did what, to which record, and when. Never store passwords, tokens or biometric data in Old/NewValues.
    [Index(nameof(EntityName), nameof(EntityId))]
    public class AuditLog
    {
        public long Id { get; set; }

        // Null for system actions.
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Required, MaxLength(50)]
        public string Action { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string EntityName { get; set; } = string.Empty;

        [Required, MaxLength(64)]
        public string EntityId { get; set; } = string.Empty;

        // JSON
        public string? OldValues { get; set; }
        public string? NewValues { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
