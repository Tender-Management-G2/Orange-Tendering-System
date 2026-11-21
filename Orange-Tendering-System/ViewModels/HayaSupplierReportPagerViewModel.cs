using Orange_Tendering_System.Services;
namespace Orange_Tendering_System.ViewModels;
public sealed record HayaSupplierReportPagerViewModel(int Page, int Total, int PageSize, string Parameter, HayaReportFilters Filters);
