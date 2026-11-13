// sara rjoub 


using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Orange_Tendering_System.ViewModels
{
    public class RoleMembershipViewModel
    {
        [Required(ErrorMessage = "Please select a user.")]
        public string UserId { get; set; } = "";

        [Required(ErrorMessage = "Please select a role.")]
        public string RoleName { get; set; } = "";

        public List<SelectListItem> Users { get; set; } = new();

        public List<SelectListItem> Roles { get; set; } = new();
    }
}
