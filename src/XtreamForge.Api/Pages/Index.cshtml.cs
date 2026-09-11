using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Api.Pages;

public sealed class IndexModel(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    ILogger<IndexModel> logger) : PageModel
{
    public string ApplicationStatus { get; private set; } = "Healthy";

    public string ApplicationDetails { get; private set; } = $"XtreamForge v{typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"}";

    public string DatabaseStatus { get; private set; } = "Checking";

    public string DatabaseDetails { get; private set; } = "Checking database connectivity.";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            var providerDisplayName = GetDatabaseProviderDisplayName(dbContext);

            DatabaseStatus = canConnect ? "Connected" : "Unavailable";
            DatabaseDetails = canConnect
                ? $"{providerDisplayName} is reachable."
                : $"{providerDisplayName} is not reachable.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to verify database connectivity.");
            DatabaseStatus = "Unavailable";
            DatabaseDetails = "The database connectivity check failed.";
        }
    }

    private static string GetDatabaseProviderDisplayName(XtreamForgeDbContext dbContext)
    {
        var providerName = dbContext.Database.ProviderName;

        if (providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "PostgreSQL";
        }

        if (providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "SQLite";
        }

        return "The configured database";
    }
}
