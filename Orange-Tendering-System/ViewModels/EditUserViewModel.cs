// sara rjoub 


using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Orange_Tendering_System.ViewModels
{
    public class EditUserViewModel
    {
        [Required]
        public string Id { get; set; } = "";

        [Required(ErrorMessage = "Full name is required.")]
        [StringLength(150, MinimumLength = 2)]
        public string FullName { get; set; } = "";

        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        public int? DepartmentId { get; set; }

        public bool IsActive { get; set; }

        // Display only - roles are managed separately
        public string CurrentRoles { get; set; } = "";

        public List<SelectListItem> Departments { get; set; } = new();
    }
}
