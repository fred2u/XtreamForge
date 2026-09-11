using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Infrastructure.Services;

public static class InfrastructureMigrationRunner
{
    public static async Task MigrateAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
