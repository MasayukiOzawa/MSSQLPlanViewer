using MSSQLPlanViewer.Web.Components;
using MSSQLPlanViewer.Web;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;
using MSSQLPlanViewer.Core.Comparison;
using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Diagnostics.Rules;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 10 * 1024 * 1024;
    });
builder.Services.AddScoped<IShowplanParser, ShowplanParser>();
builder.Services.AddScoped<IPlanGraphLayoutService, PlanGraphLayoutService>();
builder.Services.AddScoped<IPlanGraphSvgRenderer, PlanGraphSvgRenderer>();
builder.Services.AddScoped<IPlanGraphPngExporter, PlanGraphPngExporter>();
builder.Services.AddScoped<IPlanTableProjector, PlanTableProjector>();
builder.Services.AddScoped<IPlanComparisonService, PlanComparisonService>();
builder.Services.AddScoped<IPlanDiagnosticsService, PlanDiagnosticsService>();
builder.Services.AddScoped<IPlanDiagnosticRule, CardinalityEstimateSkewRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, TempDbSpillRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, ExpensiveLookupRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, HighImpactMissingIndexRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, ImplicitConversionRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, MemoryGrantMismatchRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, StaleStatisticsRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, LargeScanWithResidualPredicateRule>();
builder.Services.AddScoped<IPlanDiagnosticRule, ParallelThreadSkewRule>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapPlanExportEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
}
