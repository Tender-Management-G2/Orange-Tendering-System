
// sara rjoub 

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Orange_Tendering_System.ViewModels
{
    public class CreateUserViewModel
    {
        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(150, MinimumLength = 2)]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = "";

        public int? DepartmentId { get; set; }

        [Required(ErrorMessage = "Please select a role.")]
        public string Role { get; set; } = "";

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        public bool IsActive { get; set; } = true;

        // Dropdown lists
        public List<SelectListItem> Departments { get; set; } = new();

        public List<SelectListItem> Roles { get; set; } = new();
    }
}
