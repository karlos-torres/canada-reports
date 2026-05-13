using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using canada_reports.Models;
using System.Text;
using canada_reports.Services;

namespace canada_reports.Controllers;

public class HomeController(IReportService reportService) : Controller
{
    private const int DefaultPageSize = 10;
    private readonly IReportService _reportService = reportService;

    public async Task<IActionResult> Index(string? reportKey, int page = 1, int pageSize = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        var reports = _reportService.GetReports();

        if (reports.Count == 0)
        {
            return View(new ReportPageViewModel
            {
                Reports = reports,
                SelectedReportKey = string.Empty,
                SelectedReportName = "No reports configured",
                Columns = [],
                Rows = [],
                Page = 1,
                PageSize = pageSize,
                TotalCount = 0,
                ErrorMessage = "No report definitions were found. Configure the Reports section in appsettings."
            });
        }

        var selectedReport = reports.FirstOrDefault(r => r.Key.Equals(reportKey, StringComparison.OrdinalIgnoreCase)) ?? reports[0];
        page = Math.Max(page, 1);
        pageSize = pageSize <= 0 ? DefaultPageSize : pageSize;

        try
        {
            var result = await _reportService.GetReportPageAsync(selectedReport, page, pageSize, cancellationToken);
            return View(new ReportPageViewModel
            {
                Reports = reports,
                SelectedReportKey = selectedReport.Key,
                SelectedReportName = selectedReport.Name,
                Columns = result.Columns,
                Rows = result.Rows,
                Page = page,
                PageSize = pageSize,
                TotalCount = result.TotalCount
            });
        }
        catch (Exception ex)
        {
            return View(new ReportPageViewModel
            {
                Reports = reports,
                SelectedReportKey = selectedReport.Key,
                SelectedReportName = selectedReport.Name,
                Columns = [],
                Rows = [],
                Page = page,
                PageSize = pageSize,
                TotalCount = 0,
                ErrorMessage = $"Unable to load report data: {ex.Message}"
            });
        }
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public async Task<IActionResult> Export(string reportKey, CancellationToken cancellationToken)
    {
        var reports = _reportService.GetReports();
        var selectedReport = reports.FirstOrDefault(r => r.Key.Equals(reportKey, StringComparison.OrdinalIgnoreCase));

        if (selectedReport is null)
        {
            return NotFound("Unknown report.");
        }

        var reportData = await _reportService.GetReportAllAsync(selectedReport, cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", reportData.Columns.Select(EscapeCsvValue)));

        foreach (var row in reportData.Rows)
        {
            csv.AppendLine(string.Join(",", row.Select(EscapeCsvValue)));
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        var fileName = $"{selectedReport.Key}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }

    private static string EscapeCsvValue(object? value)
    {
        var text = value?.ToString() ?? string.Empty;
        var escaped = text.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
