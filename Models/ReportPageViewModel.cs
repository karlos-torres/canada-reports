namespace canada_reports.Models;

public class ReportPageViewModel
{
    public required IReadOnlyList<ReportDefinition> Reports { get; init; }
    public required string SelectedReportKey { get; init; }
    public required string SelectedReportName { get; init; }
    public string? ErrorMessage { get; init; }
}

public class PagedReportResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<IReadOnlyList<object?>> Rows { get; init; }
    public required int TotalCount { get; init; }
}
