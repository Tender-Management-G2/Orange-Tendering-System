using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One fingerprint / device passkey (WebAuthn) of a user. Only the PUBLIC key is stored; the private key and the
    // fingerprint itself never leave the person's device.
    [Index(nameof(CredentialId), IsUnique = true)]
    [Index(nameof(UserId))]
    public class UserPasskeyCredential
    {
        public long Id { get; set; }

        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        // base64url credential id
        [Required, MaxLength(450)]
        public string CredentialId { get; set; } = string.Empty;

        // COSE public key
        [Required]
        public byte[] PublicKey { get; set; } = [];

        // Clone detection counter.
        public uint SignCount { get; set; }

        [MaxLength(150)]
        public string? DeviceLabel { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
    }
}
