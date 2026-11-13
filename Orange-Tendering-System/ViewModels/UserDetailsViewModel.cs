// sara rjoub 



namespace Orange_Tendering_System.ViewModels
{
    public class UserDetailsViewModel
    {
        public string Id { get; set; } = "";

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";

        public string Roles { get; set; } = "";

        public string DepartmentName { get; set; } = "";

        public string SupplierName { get; set; } = "";

        public bool IsActive { get; set; }

        public bool EmailConfirmed { get; set; }

        public DateTimeOffset? LockoutEnd { get; set; }

        public List<UserAuditEntryViewModel> RecentActivities
        { get; set; } = new();
    }

    public class UserAuditEntryViewModel
    {
        public DateTime CreatedAt { get; set; }

        public string Action { get; set; } = "";

        public string EntityName { get; set; } = "";

        public string ChangedBy { get; set; } = "";
    }
}
