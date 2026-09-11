using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XtreamForge.Infrastructure.Configuration;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IHostApplicationBuilder AddXtreamForgeInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<XtreamOptions>()
            .BindConfiguration(XtreamOptions.SectionName);

        builder.Services.AddOptions<TmdbOptions>()
            .BindConfiguration(TmdbOptions.SectionName);

        builder.Services.AddDbContext<XtreamForgeDbContext>(options => ConfigureDbContext(options, builder.Configuration));
        builder.Services.AddDbContextFactory<XtreamForgeDbContext>(options => ConfigureDbContext(options, builder.Configuration));
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<XtreamForgeDbContext>(name: "database");

        return builder;
    }

    public static async Task ApplyInfrastructureMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    private static void ConfigureDbContext(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'database' is required.");
        }

        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(XtreamForgeDbContext).Assembly.FullName));
    }
}
