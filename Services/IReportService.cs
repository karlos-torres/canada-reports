using TGCa_Reports.Models;

namespace TGCa_Reports.Services;

public interface IReportService
{
    IReadOnlyList<ReportDefinition> GetReports();
    Task<PagedReportResult> GetReportPageAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string> parameterValues,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<PagedReportResult> GetReportAllAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string> parameterValues,
        CancellationToken cancellationToken);
}
