using System.Data;
using Microsoft.Data.SqlClient;
using canada_reports.Models;

namespace canada_reports.Services;

public class SqlReportService(IConfiguration configuration) : IReportService
{
    private readonly IConfiguration _configuration = configuration;

    public IReadOnlyList<ReportDefinition> GetReports()
    {
        var reports = _configuration.GetSection("Reports").Get<List<ReportDefinition>>() ?? [];
        return reports.Where(r => !string.IsNullOrWhiteSpace(r.Key) && !string.IsNullOrWhiteSpace(r.Query)).ToList();
    }

    public async Task<PagedReportResult> GetReportPageAsync(ReportDefinition report, int page, int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var countSql = $"SELECT COUNT(1) FROM ({report.Query}) AS report_data;";
        await using var countCommand = new SqlCommand(countSql, connection);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        var pageSql = $"""
                       SELECT * FROM ({report.Query}) AS report_data
                       ORDER BY (SELECT NULL)
                       OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                       """;

        await using var pageCommand = new SqlCommand(pageSql, connection);
        pageCommand.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        pageCommand.Parameters.AddWithValue("@PageSize", pageSize);

        var rows = new List<IReadOnlyList<object?>>();
        var columns = new List<string>();

        await using var reader = await pageCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return new PagedReportResult
        {
            Columns = columns,
            Rows = rows,
            TotalCount = totalCount
        };
    }

    public async Task<PagedReportResult> GetReportAllAsync(ReportDefinition report, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT * FROM ({report.Query}) AS report_data;";
        await using var command = new SqlCommand(sql, connection);

        var rows = new List<IReadOnlyList<object?>>();
        var columns = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);

        for (var i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new object?[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return new PagedReportResult
        {
            Columns = columns,
            Rows = rows,
            TotalCount = rows.Count
        };
    }

    private string GetConnectionString()
    {
        var connectionString = _configuration.GetConnectionString("ReportsDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:ReportsDatabase is not configured.");
        }

        return connectionString;
    }
}
