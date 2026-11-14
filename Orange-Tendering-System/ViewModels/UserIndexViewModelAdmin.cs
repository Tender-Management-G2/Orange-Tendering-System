
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Orange_Tendering_System.ViewModels
{
    public class UserIndexViewModel
    {
        // ==============================
        // USERS TABLE
        // ==============================

        public List<UserListViewModel> Users { get; set; } = new();


        // ==============================
        // SEARCH AND FILTERS
        // ==============================

        public string? Search { get; set; }

        public string? RoleFilter { get; set; }

        public string? DepartmentFilter { get; set; }

        public string? StatusFilter { get; set; }

        // Available departments for the dropdown
        public List<SelectListItem> Departments { get; set; } = new();


        // ==============================
        // KPI CARDS
        // ==============================

        public int TotalUsers { get; set; }

        public int ActiveUsers { get; set; }

        public int InactiveUsers { get; set; }

        public int DepartmentUsers { get; set; }


        // ==============================
        // PAGINATION
        // ==============================

        public int CurrentPage { get; set; } = 1;

        public int PageSize { get; set; } = 10;

        public int TotalRecords { get; set; }

        public int TotalPages =>
            (int)Math.Ceiling(
                (double)TotalRecords / PageSize);
    }
}
