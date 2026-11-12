using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.Models.Entities
{
    public class ClarificationMessage
    {
        public int Id { get; set; }

        public int ClarificationId { get; set; }
        public Clarification Clarification { get; set; } = null!;

        public string SenderUserId { get; set; } = string.Empty;
        public ApplicationUser SenderUser { get; set; } = null!;

        [Required]
        public string Message { get; set; } = string.Empty;

        public DateTime SentAt { get; set; }

        public ICollection<Document> Documents { get; set; } = new List<Document>();
    }
}
