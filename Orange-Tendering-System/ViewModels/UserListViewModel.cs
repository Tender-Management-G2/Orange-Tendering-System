
// sara rjoub 


using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.ViewModels
{
    public class UserListViewModel
    {
        public string Id { get; set; } = "";

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";

        public string Roles { get; set; } = "";

        public string DepartmentOrCompany { get; set; } = "";

        public bool IsActive { get; set; }

        // Same wording as the supplier's own Pending Approval page and the Supplier Administration page.
        public SupplierStatus? SupplierStatus { get; set; }

        public string StatusLabel => AccountStatusText.For(IsActive, SupplierStatus);
    }
}

namespace Orange_Tendering_System.ViewModels
{
    // One place that decides the status text so the user list, the details page and the supplier pages never disagree.
    public static class AccountStatusText
    {
        public static string For(bool isActive, SupplierStatus? supplier) =>
            !isActive ? "Inactive"
            : supplier switch
            {
                SupplierStatus.Pending => "Pending approval",
                SupplierStatus.Rejected => "Rejected",
                SupplierStatus.Suspended => "Suspended",
                _ => "Active"
            };
    }
}
