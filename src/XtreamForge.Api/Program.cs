using XtreamForge.Api.Services;
using Microsoft.Extensions.Hosting;
using XtreamForge.Api.Endpoints;
using XtreamForge.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddXtreamForgeInfrastructure();
builder.Services.AddOpenApi();
builder.Services.AddRazorPages();
builder.Services.AddXtreamEndpoints();
builder.Services.AddHostedService<DatabaseMigrationBackgroundService>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapStaticAssets();
app.MapRazorPages();
app.MapStatusEndpoints();
app.MapXtreamEndpoints();

app.Run();

public partial class Program;
