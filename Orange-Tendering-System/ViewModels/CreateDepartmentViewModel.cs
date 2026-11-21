
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Orange_Tendering_System.ViewModels
{
    public class CreateDepartmentViewModel
    {
        [Required(ErrorMessage = "Department name is required.")]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Department Name")]
        public string Name { get; set; } = "";

        [StringLength(160, ErrorMessage = "Use a short description of up to 160 characters.")]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        // Employees selected while creating a department
        public List<string> SelectedEmployeeIds { get; set; } = new();

        // Available employees for selection
        public List<SelectListItem> AvailableEmployees { get; set; } = new();
    }
}
