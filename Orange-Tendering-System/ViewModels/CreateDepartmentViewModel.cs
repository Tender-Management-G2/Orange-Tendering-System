
// sara rjoub 

using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.ViewModels
{
    public class CreateDepartmentViewModel
    {
        [Required(ErrorMessage = "Department name is required.")]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Department Name")]
        public string Name { get; set; } = "";

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
