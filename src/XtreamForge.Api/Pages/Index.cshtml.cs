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

    public string DatabaseDetails { get; private set; } = "Checking PostgreSQL connectivity.";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            DatabaseStatus = canConnect ? "Connected" : "Unavailable";
            DatabaseDetails = canConnect
                ? "PostgreSQL is reachable."
                : "PostgreSQL is not reachable.";
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to verify database connectivity.");
            DatabaseStatus = "Unavailable";
            DatabaseDetails = "The database connectivity check failed.";
        }
    }
}
