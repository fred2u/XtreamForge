using Microsoft.EntityFrameworkCore;
using XtreamForge.Infrastructure.Data;

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
            await using var scope = serviceProvider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            await dbContext.Database.MigrateAsync(stoppingToken);
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
