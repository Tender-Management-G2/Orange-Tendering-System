using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // One row per user and notification once the global event spotlight (celebration / event card) was dismissed.
    // It keeps the spotlight one-time across logins and devices; the notification itself stays in the history.
    [Index(nameof(UserId), nameof(NotificationId), IsUnique = true)]
    public class NotificationAcknowledgement
    {
        public int Id { get; set; }

        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty;

        public int NotificationId { get; set; }
        public Notification Notification { get; set; } = null!;

        // win | approval | rejection | message | returned | action
        [Required, MaxLength(20)]
        public string Kind { get; set; } = string.Empty;

        public DateTime AcknowledgedAt { get; set; }
    }
}
