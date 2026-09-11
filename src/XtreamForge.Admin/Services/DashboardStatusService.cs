using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Admin.Services;

public sealed class DashboardStatusService(
    HttpClient httpClient,
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    ILogger<DashboardStatusService> logger)
{
    public async Task<DashboardStatusModel> GetDashboardStatusAsync(CancellationToken cancellationToken = default)
    {
        var application = await GetApplicationStatusAsync(cancellationToken);
        var database = await GetDatabaseStatusAsync(cancellationToken);

        return new DashboardStatusModel(application, database);
    }

    private async Task<ComponentStatus> GetApplicationStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetFromJsonAsync<ApiStatusResponse>("api/status", cancellationToken);

            return response is null
                ? new ComponentStatus("Unavailable", "Unable to read the API status response.")
                : new ComponentStatus(response.Status, $"{response.ApplicationName} v{response.ApplicationVersion}");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to retrieve the API status.");
            return new ComponentStatus("Unavailable", "The API status endpoint could not be reached.");
        }
    }

    private async Task<ComponentStatus> GetDatabaseStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? new ComponentStatus("Connected", "PostgreSQL is reachable.")
                : new ComponentStatus("Unavailable", "PostgreSQL is not reachable.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to verify database connectivity.");
            return new ComponentStatus("Unavailable", "The database connectivity check failed.");
        }
    }

    public sealed record DashboardStatusModel(ComponentStatus Application, ComponentStatus Database);

    public sealed record ComponentStatus(string State, string Details);

    private sealed record ApiStatusResponse(string ApplicationName, string ApplicationVersion, string Status);
}
