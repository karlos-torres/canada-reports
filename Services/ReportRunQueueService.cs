using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using canada_reports.Models;
using Microsoft.Extensions.Options;

namespace canada_reports.Services;

public sealed class ReportRunQueueService(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment hostEnvironment,
    IOptions<ReportRunOptions> options,
    ILogger<ReportRunQueueService> logger) : BackgroundService, IReportRunManager
{
    private const string HistoryFileName = "history.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<ReportRunQueueService> _logger = logger;
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeRunCancellation = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Dictionary<Guid, ReportRunRecord> _runs = [];
    private readonly int _maxConcurrency = Math.Max(1, options.Value.MaxConcurrency);
    private readonly int _maxRetryCount = Math.Max(0, options.Value.MaxRetryCount);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(Math.Max(5, options.Value.TimeoutSeconds));
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(Math.Max(1, options.Value.RetryDelaySeconds));
    private readonly int _historyRetentionCount = Math.Max(10, options.Value.HistoryRetentionCount);
    private readonly string _storageFolder = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "ReportRuns");
    private readonly string _resultsFolder = Path.Combine(hostEnvironment.ContentRootPath, "App_Data", "ReportRuns", "results");

    private string HistoryFilePath => Path.Combine(_storageFolder, HistoryFileName);

    public async Task<ReportRunSummary> QueueRunAsync(ReportDefinition report, CancellationToken cancellationToken)
    {
        var record = new ReportRunRecord
        {
            Id = Guid.NewGuid(),
            ReportKey = report.Key,
            ReportName = report.Name,
            Query = report.Query,
            Status = ReportRunStatus.Queued,
            StageText = "Queued",
            RequestedAtUtc = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            NotificationMessage = $"{report.Name} queued"
        };

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _runs[record.Id] = record;
            TrimHistory();
            await PersistRunsLockedAsync(cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }

        if (!_queue.Writer.TryWrite(record.Id))
        {
            throw new InvalidOperationException("Unable to queue report run.");
        }

        return ToSummary(record);
    }

    public async Task<ReportRunSummary?> GetRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            return _runs.TryGetValue(runId, out var record) ? ToSummary(record) : null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<IReadOnlyList<ReportRunSummary>> GetRecentRunsAsync(int limit, CancellationToken cancellationToken)
    {
        var takeCount = Math.Clamp(limit, 1, _historyRetentionCount);

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            return _runs.Values
                .OrderByDescending(r => r.RequestedAtUtc)
                .Take(takeCount)
                .Select(ToSummary)
                .ToList();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        ReportRunRecord? updatedRecord = null;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (!_runs.TryGetValue(runId, out var record) || record.IsTerminal)
            {
                return false;
            }

            record.Status = ReportRunStatus.Cancelled;
            record.StageText = "Cancelled";
            record.CompletedAtUtc = DateTimeOffset.UtcNow;
            record.ErrorMessage = null;
            record.NotificationMessage = $"{record.ReportName} was cancelled";
            updatedRecord = record;
            await PersistRunsLockedAsync(cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }

        if (_activeRunCancellation.TryGetValue(runId, out var activeCancellation))
        {
            activeCancellation.Cancel();
        }

        return updatedRecord is not null;
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> GetResultFileAsync(Guid runId, CancellationToken cancellationToken)
    {
        ReportRunRecord? record;

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            _runs.TryGetValue(runId, out record);
        }
        finally
        {
            _stateLock.Release();
        }

        if (record is null || record.Status != ReportRunStatus.Completed || string.IsNullOrWhiteSpace(record.ResultFileName))
        {
            return null;
        }

        var filePath = Path.Combine(_resultsFolder, record.ResultFileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var contentType = string.IsNullOrWhiteSpace(record.ResultContentType) ? "text/csv" : record.ResultContentType;
        return (bytes, contentType, record.ResultFileName);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureStoreLoadedAsync(stoppingToken);

        var workers = Enumerable.Range(0, _maxConcurrency)
            .Select(_ => Task.Run(() => WorkerLoopAsync(stoppingToken), stoppingToken))
            .ToArray();

        await Task.WhenAll(workers);
    }

    private async Task WorkerLoopAsync(CancellationToken stoppingToken)
    {
        while (await _queue.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_queue.Reader.TryRead(out var runId))
            {
                try
                {
                    await ProcessRunAsync(runId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unhandled error while processing report run {RunId}", runId);
                }
            }
        }
    }

    private async Task ProcessRunAsync(Guid runId, CancellationToken stoppingToken)
    {
        var run = await GetRunRecordAsync(runId, stoppingToken);
        if (run is null || run.IsTerminal)
        {
            return;
        }

        for (var attempt = 1; attempt <= _maxRetryCount + 1; attempt++)
        {
            if (await IsCancelledAsync(runId, stoppingToken))
            {
                return;
            }

            await MarkRunningAsync(runId, attempt, stoppingToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(_timeout);
            _activeRunCancellation[runId] = timeoutCts;

            try
            {
                var reportData = await ExecuteReportAsync(runId, timeoutCts.Token);
                await MarkFinalizingAsync(runId, stoppingToken);

                var fileName = await PersistResultAsync(runId, reportData, timeoutCts.Token);
                await MarkCompletedAsync(runId, fileName, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await MarkCancelledAsync(runId, "Stopped by host shutdown", stoppingToken);
                return;
            }
            catch (OperationCanceledException)
            {
                if (await IsCancelledAsync(runId, stoppingToken))
                {
                    await MarkCancelledAsync(runId, "Cancelled", stoppingToken);
                    return;
                }

                if (attempt <= _maxRetryCount)
                {
                    await MarkQueuedForRetryAsync(runId, "Timed out, retrying", stoppingToken);
                    await Task.Delay(_retryDelay, stoppingToken);
                    continue;
                }

                await MarkFailedAsync(runId, "Timed out", stoppingToken);
                return;
            }
            catch (Exception ex)
            {
                if (attempt <= _maxRetryCount)
                {
                    await MarkQueuedForRetryAsync(runId, $"Failed, retrying: {ex.Message}", stoppingToken);
                    await Task.Delay(_retryDelay, stoppingToken);
                    continue;
                }

                await MarkFailedAsync(runId, ex.Message, stoppingToken);
                return;
            }
            finally
            {
                if (_activeRunCancellation.TryRemove(runId, out var activeToken))
                {
                    activeToken.Dispose();
                }
            }
        }
    }

    private async Task<PagedReportResult> ExecuteReportAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await GetRunRecordAsync(runId, cancellationToken)
                  ?? throw new InvalidOperationException("Report run not found.");

        using var scope = _scopeFactory.CreateScope();
        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();

        var reportDefinition = new ReportDefinition
        {
            Key = run.ReportKey,
            Name = run.ReportName,
            Query = run.Query
        };

        return await reportService.GetReportAllAsync(reportDefinition, cancellationToken);
    }

    private async Task<string> PersistResultAsync(Guid runId, PagedReportResult reportData, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_resultsFolder);

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine(string.Join(',', reportData.Columns.Select(EscapeCsvValue)));

        foreach (var row in reportData.Rows)
        {
            csvBuilder.AppendLine(string.Join(',', row.Select(EscapeCsvValue)));
        }

        var fileName = $"{runId:N}.csv";
        var filePath = Path.Combine(_resultsFolder, fileName);
        await File.WriteAllTextAsync(filePath, csvBuilder.ToString(), Encoding.UTF8, cancellationToken);

        return fileName;
    }

    private static string EscapeCsvValue(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        var escaped = text.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private async Task<ReportRunRecord?> GetRunRecordAsync(Guid runId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            return _runs.TryGetValue(runId, out var run) ? run : null;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task<bool> IsCancelledAsync(Guid runId, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            return _runs.TryGetValue(runId, out var run) && run.Status == ReportRunStatus.Cancelled;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task MarkRunningAsync(Guid runId, int attempt, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, run =>
        {
            run.Status = ReportRunStatus.Running;
            run.StageText = "Executing";
            run.StartedAtUtc ??= DateTimeOffset.UtcNow;
            run.AttemptCount = attempt;
            run.ErrorMessage = null;
            run.NotificationMessage = $"{run.ReportName} is running";
        }, cancellationToken);
    }

    private async Task MarkFinalizingAsync(Guid runId, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, run =>
        {
            run.Status = ReportRunStatus.Running;
            run.StageText = "Finalizing";
            run.NotificationMessage = $"{run.ReportName} is finalizing";
        }, cancellationToken);
    }

    private async Task MarkCompletedAsync(Guid runId, string fileName, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, run =>
        {
            run.Status = ReportRunStatus.Completed;
            run.StageText = "Completed";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.ResultFileName = fileName;
            run.ResultContentType = "text/csv";
            run.NotificationMessage = $"{run.ReportName} completed";
        }, cancellationToken);
    }

    private async Task MarkCancelledAsync(Guid runId, string reason, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, run =>
        {
            run.Status = ReportRunStatus.Cancelled;
            run.StageText = "Cancelled";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.ErrorMessage = reason;
            run.NotificationMessage = $"{run.ReportName} was cancelled";
        }, cancellationToken);
    }

    private async Task MarkFailedAsync(Guid runId, string error, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, run =>
        {
            run.Status = ReportRunStatus.Failed;
            run.StageText = "Failed";
            run.CompletedAtUtc = DateTimeOffset.UtcNow;
            run.ErrorMessage = error;
            run.NotificationMessage = $"{run.ReportName} failed";
        }, cancellationToken);
    }

    private async Task MarkQueuedForRetryAsync(Guid runId, string message, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, run =>
        {
            run.Status = ReportRunStatus.Queued;
            run.StageText = "Queued";
            run.ErrorMessage = message;
            run.NotificationMessage = $"{run.ReportName} retry queued";
        }, cancellationToken);
    }

    private async Task UpdateRunAsync(Guid runId, Action<ReportRunRecord> update, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            if (_runs.TryGetValue(runId, out var run))
            {
                update(run);
                TrimHistory();
                await PersistRunsLockedAsync(cancellationToken);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task EnsureStoreLoadedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_storageFolder);
        Directory.CreateDirectory(_resultsFolder);

        if (!File.Exists(HistoryFilePath))
        {
            return;
        }

        await _stateLock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(HistoryFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var persisted = JsonSerializer.Deserialize<List<ReportRunRecord>>(json, JsonOptions) ?? [];
            foreach (var run in persisted)
            {
                if (!run.IsTerminal)
                {
                    run.Status = ReportRunStatus.Cancelled;
                    run.StageText = "Cancelled";
                    run.ErrorMessage = "Cancelled after application restart";
                    run.CompletedAtUtc ??= DateTimeOffset.UtcNow;
                    run.NotificationMessage = $"{run.ReportName} cancelled after restart";
                }

                _runs[run.Id] = run;
            }

            TrimHistory();
            await PersistRunsLockedAsync(cancellationToken);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void TrimHistory()
    {
        if (_runs.Count <= _historyRetentionCount)
        {
            return;
        }

        var toRemove = _runs.Values
            .OrderByDescending(r => r.RequestedAtUtc)
            .Skip(_historyRetentionCount)
            .Select(r => r.Id)
            .ToList();

        foreach (var id in toRemove)
        {
            if (_runs.Remove(id, out var removed) && !string.IsNullOrWhiteSpace(removed.ResultFileName))
            {
                var filePath = Path.Combine(_resultsFolder, removed.ResultFileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }
    }

    private async Task PersistRunsLockedAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_storageFolder);
        var snapshot = _runs.Values
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToList();

        var tempFile = Path.Combine(_storageFolder, $"{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(tempFile, json, cancellationToken);
        File.Move(tempFile, HistoryFilePath, overwrite: true);
    }

    private static ReportRunSummary ToSummary(ReportRunRecord run)
    {
        return new ReportRunSummary
        {
            Id = run.Id,
            ReportKey = run.ReportKey,
            ReportName = run.ReportName,
            Status = run.Status,
            StageText = run.StageText,
            RequestedAtUtc = run.RequestedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            CompletedAtUtc = run.CompletedAtUtc,
            AttemptCount = run.AttemptCount,
            ErrorMessage = run.ErrorMessage,
            NotificationMessage = run.NotificationMessage,
            HasRealProgress = run.HasRealProgress,
            CanDownload = run.Status == ReportRunStatus.Completed && !string.IsNullOrWhiteSpace(run.ResultFileName),
            CanCancel = run.Status is ReportRunStatus.Queued or ReportRunStatus.Running,
            DurationSeconds = run.DurationSeconds
        };
    }
}
