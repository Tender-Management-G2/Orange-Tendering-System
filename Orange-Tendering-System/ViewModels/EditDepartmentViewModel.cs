// sara rjoub 

using System.ComponentModel.DataAnnotations;

namespace Orange_Tendering_System.ViewModels
{
    public class EditDepartmentViewModel
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Department name is required.")]
        [StringLength(100, MinimumLength = 2)]
        [Display(Name = "Department Name")]
        public string Name { get; set; } = "";

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; }
    }
}

// الفرق بين هذا والـCreate ViewModel هو وجود Id، لأننا هون بدنا نحدد أي Department رح نعدل.