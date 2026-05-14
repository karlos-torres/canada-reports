using canada_reports.Models;

namespace canada_reports.Services;

public interface IReportService
{
    IReadOnlyList<ReportDefinition> GetReports();
    Task<PagedReportResult> GetReportPageAsync(ReportDefinition report, int page, int pageSize, CancellationToken cancellationToken);
    Task<PagedReportResult> GetReportAllAsync(ReportDefinition report, CancellationToken cancellationToken);
}
