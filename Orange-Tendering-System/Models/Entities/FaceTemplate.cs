using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // A numeric face descriptor (128 numbers computed in the browser), encrypted with ASP.NET Core Data Protection.
    // No photo or video is ever stored or uploaded.
    [Index(nameof(UserId))]
    public class FaceTemplate
    {
        public long Id { get; set; }

        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        [Required]
        public byte[] ProtectedTemplate { get; set; } = [];

        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
    }
}
