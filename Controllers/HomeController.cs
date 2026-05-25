using System.Diagnostics;
using canada_reports.Models;
using canada_reports.Services;
using Microsoft.AspNetCore.Mvc;

namespace canada_reports.Controllers;

public class HomeController(IReportService reportService) : Controller
{
    private readonly IReportService _reportService = reportService;

    public IActionResult Index(string? reportKey)
    {
        var reports = _reportService.GetReports();

        if (reports.Count == 0)
        {
            return View(new ReportPageViewModel
            {
                Reports = reports,
                SelectedReportKey = string.Empty,
                SelectedReportName = "No reports configured",
                ErrorMessage = "No report definitions were found. Configure the Reports section in appsettings."
            });
        }

        var selectedReport = reports.FirstOrDefault(r =>
            r.Key.Equals(reportKey, StringComparison.OrdinalIgnoreCase)) ?? reports[0];

        return View(new ReportPageViewModel
        {
            Reports = reports,
            SelectedReportKey = selectedReport.Key,
            SelectedReportName = selectedReport.Name,
            ErrorMessage = null
        });
    }

    public IActionResult Dashboard(string? reportKey)
    {
        var reports = _reportService.GetReports();

        if (reports.Count == 0)
        {
            return View(new ReportPageViewModel
            {
                Reports = reports,
                SelectedReportKey = string.Empty,
                SelectedReportName = "No reports configured",
                ErrorMessage = "No report definitions were found. Configure the Reports section in appsettings."
            });
        }

        var selectedReport = reports.FirstOrDefault(r =>
            r.Key.Equals(reportKey, StringComparison.OrdinalIgnoreCase)) ?? reports[0];

        return View(new ReportPageViewModel
        {
            Reports = reports,
            SelectedReportKey = selectedReport.Key,
            SelectedReportName = selectedReport.Name,
            ErrorMessage = null
        });
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
}
