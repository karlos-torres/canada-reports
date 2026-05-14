namespace canada_reports.Models;

public class ReportDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<ReportParameterDefinition> Parameters { get; init; } = [];
}

public class ReportParameterDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string ControlType { get; init; }
    public bool Required { get; init; }
    public string? DefaultValue { get; init; }
    public string? Placeholder { get; init; }
    public IReadOnlyList<ReportParameterOption> Options { get; init; } = [];
}

public class ReportParameterOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}
