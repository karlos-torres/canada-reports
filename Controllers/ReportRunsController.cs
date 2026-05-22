using canada_reports.Models;
using canada_reports.Services;
using Microsoft.AspNetCore.Mvc;

namespace canada_reports.Controllers;

[ApiController]
[Route("api/report-runs")]
public class ReportRunsController(IReportService reportService, IReportRunManager runManager) : ControllerBase
{
    private readonly IReportService _reportService = reportService;
    private readonly IReportRunManager _runManager = runManager;

    [HttpPost]
    public async Task<IActionResult> Queue([FromBody] CreateReportRunRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReportKey))
        {
            return BadRequest("reportKey is required.");
        }

        var report = _reportService.GetReports().FirstOrDefault(r =>
            r.Key.Equals(request.ReportKey, StringComparison.OrdinalIgnoreCase));

        if (report is null)
        {
            return NotFound("Unknown report.");
        }

        var createdRun = await _runManager.QueueRunAsync(report, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { runId = createdRun.Id }, createdRun);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReportRunSummary>>> GetRecent([FromQuery] int limit = 25, CancellationToken cancellationToken = default)
    {
        var runs = await _runManager.GetRecentRunsAsync(limit, cancellationToken);
        return Ok(runs);
    }

    [HttpGet("{runId:guid}")]
    public async Task<ActionResult<ReportRunSummary>> GetById(Guid runId, CancellationToken cancellationToken)
    {
        var run = await _runManager.GetRunAsync(runId, cancellationToken);
        if (run is null)
        {
            return NotFound();
        }

        return Ok(run);
    }

    [HttpPost("{runId:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid runId, CancellationToken cancellationToken)
    {
        var cancelled = await _runManager.CancelRunAsync(runId, cancellationToken);
        return cancelled ? NoContent() : NotFound();
    }

    [HttpGet("{runId:guid}/download")]
    public async Task<IActionResult> Download(Guid runId, CancellationToken cancellationToken)
    {
        var result = await _runManager.GetResultFileAsync(runId, cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    public sealed class CreateReportRunRequest
    {
        public string ReportKey { get; set; } = string.Empty;
    }
}
