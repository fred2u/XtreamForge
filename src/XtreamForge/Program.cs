using Microsoft.EntityFrameworkCore;
using System.Net;
using XtreamForge.Admin;
using XtreamForge.Categories;
using XtreamForge.Configuration;
using XtreamForge.Data;
using XtreamForge.Endpoints;
using XtreamForge.Items;
using XtreamForge.Source;
using XtreamForge.Xtream;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

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

builder.Services.AddSingleton<CategoryRuleEvaluator>();
builder.Services.AddSingleton<ItemRuleEvaluator>();
builder.Services.AddSingleton<SourceService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<CategoryRuleService>();
builder.Services.AddScoped<ItemRuleService>();
builder.Services.AddSingleton<StreamTmdbMappingService>();
builder.Services.AddScoped<XtreamCategoryMappingService>();
builder.Services.AddSingleton(_ =>
{
    var handler = new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false
    };

    return new XtreamUpstreamClient(new HttpClient(handler));
});
builder.Services.AddScoped<ForwarderService>();
builder.Services.AddScoped<XtreamCategoryProxyService>();
builder.Services.AddScoped<XtreamContentProxyService>();
builder.Services.AddScoped<XtreamSourceDiscoveryService>();
builder.Services.AddSingleton<TmdbResolutionQueue>();
builder.Services.AddHostedService<TmdbResolutionBackgroundService>();
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAdminEndpoints();
app.MapStatusEndpoints();
app.MapXtreamEndpoints();

app.Run();

public partial class Program;
