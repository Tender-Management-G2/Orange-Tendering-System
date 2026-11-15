using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Orange_Tendering_System.Models.Entities
{
    // Floating AI Chat Assistant history. Strictly per user: every query filters by UserId, so a user can never read, continue or
    // delete another user's conversation. This is separate from AI Bid Analysis (AiAnalysis) and from the role-based Clarifications.
    [Index(nameof(UserId), nameof(UpdatedAt))]
    public class AiChatConversation
    {
        public int Id { get; set; }
        [Required, MaxLength(450)] public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;
        [Required, MaxLength(200)] public string Title { get; set; } = "New chat";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public ICollection<AiChatMessage> Messages { get; set; } = new List<AiChatMessage>();
    }

    [Index(nameof(ConversationId), nameof(Id))]
    public class AiChatMessage
    {
        public long Id { get; set; }
        public int ConversationId { get; set; }
        public AiChatConversation Conversation { get; set; } = null!;
        [Required, MaxLength(20)] public string Role { get; set; } = "user";   // user | assistant
        [Required] public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
