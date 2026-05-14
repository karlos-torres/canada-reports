namespace canada_reports.Models;

public class ReportDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Query { get; init; }
}
