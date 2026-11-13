// sara rjoub 



namespace Orange_Tendering_System.ViewModels
{
    public class UserIndexViewModel
    {
        public List<UserListViewModel> Users { get; set; } = new();

        public string? Search { get; set; }

        public string? RoleFilter { get; set; }

        public string? StatusFilter { get; set; }

        public int CurrentPage { get; set; } = 1;

        public int PageSize { get; set; } = 10;

        public int TotalRecords { get; set; }

        public int TotalPages =>
            (int)Math.Ceiling(
                (double)TotalRecords / PageSize);
    }
}
