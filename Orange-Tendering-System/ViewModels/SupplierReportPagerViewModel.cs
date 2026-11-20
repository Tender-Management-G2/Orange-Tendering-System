using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.ViewModels;
public sealed record SupplierReportPagerViewModel(int Page, int Total, int PageSize, string Parameter, ReportFilters Filters);
