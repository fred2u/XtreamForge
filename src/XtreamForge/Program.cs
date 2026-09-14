using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;
using XtreamForge.Components;
using XtreamForge.Configuration;
using XtreamForge.Data;
using XtreamForge.Endpoints;
using XtreamForge.Xtream;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOptions<XtreamOptions>()
    .BindConfiguration(XtreamOptions.SectionName);
builder.Services.AddOptions<TmdbOptions>()
    .BindConfiguration(TmdbOptions.SectionName);
builder.Services.AddOptions<XtreamProxyOptions>()
    .BindConfiguration(XtreamProxyOptions.SectionName);

builder.Services.AddDbContextFactory<XtreamForgeDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("database");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Connection string 'database' is required.");
    }

    if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(XtreamForgeDbContext).Assembly.FullName));
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});
builder.Services.AddScoped(static serviceProvider =>
    serviceProvider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<XtreamForgeDbContext>(name: "database");

builder.Services.AddHttpClient(ForwarderService.HttpClientName);
builder.Services.AddSingleton<CategoryRuleEvaluator>();
builder.Services.AddScoped<CategoryRuleService>();
builder.Services.AddScoped<XtreamCategoryMappingService>();
builder.Services.AddScoped<ForwarderService>();
builder.Services.AddScoped<XtreamCategoryProxyService>();
builder.Services.AddScoped<XtreamContentProxyService>();
builder.Services.AddSingleton<XtreamUpstreamDestinationResolver>();
builder.Services.AddSingleton<XtreamRequestClassifier>();

var app = builder.Build();

if (app.Configuration.GetValue("Database:ApplyMigrations", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapDefaultEndpoints();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapStatusEndpoints();
app.MapXtreamEndpoints();

app.Run();

public partial class Program;
