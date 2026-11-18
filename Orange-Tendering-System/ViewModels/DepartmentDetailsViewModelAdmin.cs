
namespace Orange_Tendering_System.ViewModels
{
    public class DepartmentDetailsViewModel
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public string? Description { get; set; }

        public bool IsActive { get; set; }

        public int EmployeeCount { get; set; }

        public int PurchaseRequestCount { get; set; }

        public List<DepartmentEmployeeViewModel> Employees
        { get; set; } = new();
    }

    public class DepartmentEmployeeViewModel
    {
        public string Id { get; set; } = "";

        public string FullName { get; set; } = "";

        public string Email { get; set; } = "";

        public bool IsActive { get; set; }
    }
}
