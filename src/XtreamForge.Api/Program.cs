using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using XtreamForge.Api.Services;
using Microsoft.Extensions.Hosting;
using XtreamForge.Api.Endpoints;
using XtreamForge.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.AddServiceDefaults();
builder.AddXtreamForgeInfrastructure();
builder.Services.AddOpenApi();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.ConfigureFilter(new AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddServerSideBlazor();
builder.Services.AddXtreamEndpoints();
builder.Services.AddHostedService<DatabaseMigrationBackgroundService>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapStaticAssets();
app.MapBlazorHub();
app.MapRazorPages();
app.MapStatusEndpoints();
app.MapXtreamEndpoints();

app.Run();

public partial class Program;
