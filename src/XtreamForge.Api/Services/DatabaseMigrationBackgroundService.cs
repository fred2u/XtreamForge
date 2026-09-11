using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Api.Services;

public sealed class DatabaseMigrationBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<DatabaseMigrationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Database:ApplyMigrations", true))
        {
            return;
        }

        try
        {
            await InfrastructureMigrationRunner.MigrateAsync(serviceProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Database migrations could not be applied during background startup.");
            throw;
        }
    }
}
