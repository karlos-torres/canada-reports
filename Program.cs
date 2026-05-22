using canada_reports.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IReportService, SqlReportService>();
builder.Services.Configure<ReportRunOptions>(builder.Configuration.GetSection("ReportRuns"));
builder.Services.AddSingleton<ReportRunQueueService>();
builder.Services.AddSingleton<IReportRunManager>(sp => sp.GetRequiredService<ReportRunQueueService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReportRunQueueService>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseRouting();

app.UseAuthorization();

app.UseStaticFiles();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
