// sara rjoub 


namespace Orange_Tendering_System.ViewModels
{
    public class RoleListViewModel
    {
        public string RoleName { get; set; } = "";

        public string Purpose { get; set; } = "";

        public int UserCount { get; set; }

        public bool IsSeeded { get; set; }

        public List<string> Users { get; set; } = new();
    }
}
