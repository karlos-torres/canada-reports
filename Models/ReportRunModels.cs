namespace canada_reports.Models;

public enum ReportRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class ReportRunRecord
{
    public Guid Id { get; set; }
    public string ReportKey { get; set; } = string.Empty;
    public string ReportName { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public ReportRunStatus Status { get; set; } = ReportRunStatus.Queued;
    public string StageText { get; set; } = "Queued";
    public DateTimeOffset RequestedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultFileName { get; set; }
    public string? ResultContentType { get; set; }
    public string? NotificationMessage { get; set; }
    public bool HasRealProgress { get; set; }

    public double? DurationSeconds =>
        StartedAtUtc is null || CompletedAtUtc is null
            ? null
            : Math.Round((CompletedAtUtc.Value - StartedAtUtc.Value).TotalSeconds, 2);

    public bool IsTerminal =>
        Status is ReportRunStatus.Completed or ReportRunStatus.Failed or ReportRunStatus.Cancelled;
}

public sealed class ReportRunSummary
{
    public required Guid Id { get; init; }
    public required string ReportKey { get; init; }
    public required string ReportName { get; init; }
    public required ReportRunStatus Status { get; init; }
    public required string StageText { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public int AttemptCount { get; init; }
    public string? ErrorMessage { get; init; }
    public string? NotificationMessage { get; init; }
    public bool HasRealProgress { get; init; }
    public bool CanDownload { get; init; }
    public bool CanCancel { get; init; }
    public double? DurationSeconds { get; init; }
}
