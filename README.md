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

Configure SQL Server connection and report SQL in `appsettings.json`:

- `ConnectionStrings:ReportsDatabase`
- `Reports` (array of `{ Key, Name, Query, Parameters[] }`)

Each report parameter supports:

- `Key`: SQL parameter name without `@` (for example `StartDate` maps to `@StartDate`)
- `Label`: UI label
- `ControlType`: `date`, `number`, `dropdown`/`select`, `textbox`
- `Required`, `DefaultValue`, `Placeholder`
- `Options` (for dropdown/select): array of `{ Value, Label }`
