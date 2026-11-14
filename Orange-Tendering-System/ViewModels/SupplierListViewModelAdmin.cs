
// sara rjoub 

namespace Orange_Tendering_System.ViewModels
{
    
        public class SupplierListViewModel
        {
            public int Id { get; set; }

            public string CompanyName { get; set; } = "";

            public string RegistrationNumber { get; set; } = "";

            public string Email { get; set; } = "";

            public string Status { get; set; } = "";

            public DateTime CreatedAt { get; set; }

            public string ReviewedBy { get; set; } = "";

            public int DocumentCount { get; set; }
        }

        public class SupplierIndexViewModel
        {
            public List<SupplierListViewModel> Suppliers { get; set; } = new();

            public string? Search { get; set; }

            public string? StatusFilter { get; set; }

            public int PendingCount { get; set; }

            public int ApprovedCount { get; set; }

            public int RejectedCount { get; set; }

            public int SuspendedCount { get; set; }

            public int CurrentPage { get; set; } = 1;

            public int TotalRecords { get; set; }

            public int PageSize { get; set; } = 10;

            public int TotalPages =>
                (int)Math.Ceiling((double)TotalRecords / PageSize);
        }
    }
