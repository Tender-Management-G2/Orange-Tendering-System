
// sara rjoub 
namespace Orange_Tendering_System.ViewModels
{
    public class DepartmentListViewModel
    {


            public int Id { get; set; }

            public string Name { get; set; } = "";

            public string? Description { get; set; }

            public bool IsActive { get; set; }

            public int EmployeeCount { get; set; }

            public int PurchaseRequestCount { get; set; }
        }
    }


