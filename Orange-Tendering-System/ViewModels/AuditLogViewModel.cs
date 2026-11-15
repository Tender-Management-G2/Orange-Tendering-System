
// sara rjoub 



namespace Orange_Tendering_System.ViewModels
{
    public class AuditLogViewModel
    {
        public long Id { get; set; }

        public string UserName { get; set; } = "";

        public string Action { get; set; } = "";

        public string EntityName { get; set; } = "";

        public string EntityId { get; set; } = "";

        public string? OldValues { get; set; }

        public string? NewValues { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
