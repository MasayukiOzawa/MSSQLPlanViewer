using MSSQLPlanViewer.Web.Components;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;
using MSSQLPlanViewer.Core.Comparison;

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
builder.Services.AddScoped<IPlanTableProjector, PlanTableProjector>();
builder.Services.AddScoped<IPlanComparisonService, PlanComparisonService>();

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
