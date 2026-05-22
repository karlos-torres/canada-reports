using canada_reports.Models;

namespace canada_reports.Services;

public interface IReportRunManager
{
    Task<ReportRunSummary> QueueRunAsync(ReportDefinition report, CancellationToken cancellationToken);
    Task<ReportRunSummary?> GetRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportRunSummary>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken);
    Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken);
    Task<(byte[] Content, string ContentType, string FileName)?> GetResultFileAsync(Guid runId, CancellationToken cancellationToken);
}
