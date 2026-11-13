
// sara rjoub 


namespace Orange_Tendering_System.ViewModels
{
    public class AuditLogPageViewModel
    {
        public List<AuditLogViewModel> Logs { get; set; } = new();

        public string? Search { get; set; }

        public string? Action { get; set; }

        public DateTime? FromDate { get; set; }

        public DateTime? ToDate { get; set; }

        public int CurrentPage { get; set; } = 1;

        public int PageSize { get; set; } = 20;

        public int TotalRecords { get; set; }

        public int TotalPages =>
            (int)Math.Ceiling((double)TotalRecords / PageSize);
    }
}


//جديد؟ليش أنشأنا ViewModel == لأنه AuditLogViewModel بيمثل سجل عملية واحدة

// بينما AuditLogPageViewModel بيمثل الصفحة كاملة، بما فيها السجلات والفلاتر وأرقام الصفحات.