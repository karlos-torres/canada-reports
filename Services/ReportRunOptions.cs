namespace canada_reports.Services;

public sealed class ReportRunOptions
{
    public int MaxConcurrency { get; set; } = 2;
    public int MaxRetryCount { get; set; } = 1;
    public int TimeoutSeconds { get; set; } = 300;
    public int RetryDelaySeconds { get; set; } = 3;
    public int HistoryRetentionCount { get; set; } = 200;
}
