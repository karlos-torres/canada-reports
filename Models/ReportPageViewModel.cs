namespace canada_reports.Models;

public class ReportPageViewModel
{
    public required IReadOnlyList<ReportDefinition> Reports { get; init; }
    public required string SelectedReportKey { get; init; }
    public required string SelectedReportName { get; init; }
    public required IReadOnlyList<ReportParameterDefinition> SelectedReportParameters { get; init; }
    public required IReadOnlyDictionary<string, string> ParameterValues { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public string? ErrorMessage { get; init; }

    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class PagedReportResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public required int TotalCount { get; init; }
}
