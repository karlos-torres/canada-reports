# canada-reports

ASP.NET Core dashboard that:

- Shows a left-side menu with report names
- Loads selected report data from SQL Server
- Displays report data in a paginated table
- Exports selected report data as a CSV file (Excel-compatible)

## Run

```bash
dotnet run
```

Configure SQL Server connection and report SQL in `/home/runner/work/canada-reports/canada-reports/appsettings.json`:

- `ConnectionStrings:ReportsDatabase`
- `Reports` (array of `{ Key, Name, Query }`)
