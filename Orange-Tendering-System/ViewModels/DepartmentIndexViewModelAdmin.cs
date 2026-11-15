
namespace Orange_Tendering_System.ViewModels
{
    public class DepartmentIndexViewModel
    {
        // Departments displayed in the table
        public List<DepartmentListViewModel> Departments
        { get; set; } = new();

        // Search and filters
        public string? Search { get; set; }

        public string? StatusFilter { get; set; }

        // KPI Cards
        public int TotalDepartments { get; set; }

        public int ActiveDepartments { get; set; }

        public int InactiveDepartments { get; set; }

        public int AssignedEmployees { get; set; }
    }
}
