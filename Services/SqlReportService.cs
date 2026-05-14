using System.Data;
using Microsoft.Data.SqlClient;
using canada_reports.Models;
using System.Globalization;

namespace canada_reports.Services;

public class SqlReportService(IConfiguration configuration) : IReportService
{
    private readonly IConfiguration _configuration = configuration;

    public IReadOnlyList<ReportDefinition> GetReports()
    {
        var reports = _configuration.GetSection("Reports").Get<List<ReportDefinition>>() ?? [];
        return reports.Where(r => !string.IsNullOrWhiteSpace(r.Key) && !string.IsNullOrWhiteSpace(r.Query)).ToList();
    }

    public async Task<PagedReportResult> GetReportPageAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string> parameterValues,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var safeQuery = GetSafeReportQuery(report.Query);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var countSql = $"SELECT COUNT(1) FROM ({safeQuery}) AS report_data;";
        await using var countCommand = new SqlCommand(countSql, connection);
        AddReportParameters(countCommand, report, parameterValues);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        var pageSql = $"""
                       SELECT * FROM ({safeQuery}) AS report_data
                       ORDER BY (SELECT NULL)
                       OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                       """;

        await using var pageCommand = new SqlCommand(pageSql, connection);
        pageCommand.Parameters.AddWithValue("@Offset", (page - 1) * pageSize);
        pageCommand.Parameters.AddWithValue("@PageSize", pageSize);
        AddReportParameters(pageCommand, report, parameterValues);

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

    public async Task<PagedReportResult> GetReportAllAsync(
        ReportDefinition report,
        IReadOnlyDictionary<string, string> parameterValues,
        CancellationToken cancellationToken)
    {
        var safeQuery = GetSafeReportQuery(report.Query);
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var sql = $"SELECT * FROM ({safeQuery}) AS report_data;";
        await using var command = new SqlCommand(sql, connection);
        AddReportParameters(command, report, parameterValues);

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
            !upperQuery.StartsWith("SELECT ") ||
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
            upperQuery.Contains("EXEC ") ||
            upperQuery.Contains("MERGE "))
        {
            throw new InvalidOperationException("Only single SELECT report queries are allowed.");
        }

        return cleanedQuery;
    }

    private static void AddReportParameters(
        SqlCommand command,
        ReportDefinition report,
        IReadOnlyDictionary<string, string> parameterValues)
    {
        foreach (var parameter in report.Parameters)
        {
            var hasValue = parameterValues.TryGetValue(parameter.Key, out var rawValue);
            var valueToUse = hasValue ? rawValue : parameter.DefaultValue;
            if (string.IsNullOrWhiteSpace(valueToUse))
            {
                if (parameter.Required)
                {
                    throw new InvalidOperationException($"Parameter '{parameter.Label}' is required.");
                }

                command.Parameters.AddWithValue($"@{parameter.Key}", DBNull.Value);
                continue;
            }

            switch (parameter.ControlType.ToLowerInvariant())
            {
                case "date":
                    if (!DateOnly.TryParse(valueToUse, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                    {
                        throw new InvalidOperationException($"Invalid date value for parameter '{parameter.Label}'.");
                    }

                    command.Parameters.AddWithValue($"@{parameter.Key}", parsedDate.ToDateTime(TimeOnly.MinValue));
                    break;
                case "number":
                    if (!decimal.TryParse(valueToUse, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedNumber))
                    {
                        throw new InvalidOperationException($"Invalid number value for parameter '{parameter.Label}'.");
                    }

                    command.Parameters.AddWithValue($"@{parameter.Key}", parsedNumber);
                    break;
                case "dropdown":
                case "select":
                    if (parameter.Options.Count > 0 &&
                        parameter.Options.All(o => !string.Equals(o.Value, valueToUse, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException($"Invalid option selected for parameter '{parameter.Label}'.");
                    }

                    command.Parameters.AddWithValue($"@{parameter.Key}", valueToUse);
                    break;
                default:
                    command.Parameters.AddWithValue($"@{parameter.Key}", valueToUse);
                    break;
            }
        }
    }
}
