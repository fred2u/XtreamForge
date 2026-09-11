using Microsoft.Extensions.Hosting;
using XtreamForge.Api.Endpoints;
using XtreamForge.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddXtreamForgeInfrastructure();
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapStatusEndpoints();

if (app.Configuration.GetValue("Database:ApplyMigrations", true))
{
    await app.ApplyInfrastructureMigrationsAsync();
}

app.Run();

public partial class Program;
