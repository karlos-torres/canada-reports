using System.Data;
using Microsoft.Data.SqlClient;
using canada_reports.Models;
using System.Text;

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
        var safeQuery = GetSafeReportQuery(report.Query);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var countSql = $"SELECT COUNT(1) FROM ({safeQuery}) AS report_data;";
        await using var countCommand = new SqlCommand(countSql, connection);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        var pageSql = $"""
                       SELECT * FROM ({safeQuery}) AS report_data
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
        var safeQuery = GetSafeReportQuery(report.Query);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        StringBuilder sbQuery = new StringBuilder();
        sbQuery.AppendLine("CREATE TABLE #Result (Id INT, SKU VARCHAR(50), DisplayName VARCHAR(MAX), Description VARCHAR(MAX), ProductType VARCHAR(1000), ProductTypeOrder INT, StandaloneSku VARCHAR(100), StandaloneSkupct DECIMAL(11,2)) ");
        sbQuery.AppendLine($"INSERT #Result EXEC {safeQuery} ");
        sbQuery.AppendLine("SELECT * FROM #Result AS report_data;");
        sbQuery.AppendLine("DROP TABLE #Result;");
        
        //var sql = $"SELECT * FROM ({safeQuery}) AS report_data;";
        var sql = sbQuery.ToString();
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

    private static string GetSafeReportQuery(string query)
    {
        var cleanedQuery = query.Trim();
        if (cleanedQuery.EndsWith(';'))
        {
            cleanedQuery = cleanedQuery[..^1].TrimEnd();
        }

        var upperQuery = cleanedQuery.ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(cleanedQuery) ||
            //!upperQuery.StartsWith("SELECT ") ||
            cleanedQuery.Contains(';') ||
            upperQuery.Contains("--") ||
            upperQuery.Contains("/*") ||
            upperQuery.Contains("*/") ||
            upperQuery.Contains("INSERT ") ||
            upperQuery.Contains("UPDATE ") ||
            upperQuery.Contains("DELETE ") ||
            upperQuery.Contains("DROP ") ||
            upperQuery.Contains("ALTER ") ||
            upperQuery.Contains("TRUNCATE ") ||
            //upperQuery.Contains("EXEC ") ||
            upperQuery.Contains("MERGE "))
        {
            throw new InvalidOperationException("Only single SELECT report queries are allowed.");
        }

        return cleanedQuery;
    }
}
