using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XtreamForge.Infrastructure.Configuration;
using XtreamForge.Infrastructure.Data;
using XtreamForge.Infrastructure.Services;

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

        builder.Services.AddDbContextFactory<XtreamForgeDbContext>(options => ConfigureDbContext(options, builder.Configuration));
        builder.Services.AddScoped(static serviceProvider =>
            serviceProvider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
        builder.Services.AddSingleton<CategoryRuleEvaluator>();
        builder.Services.AddScoped<CategoryRuleService>();
        builder.Services.AddScoped<XtreamCategoryMappingService>();
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<XtreamForgeDbContext>(name: "database");

        return builder;
    }

    public static async Task ApplyInfrastructureMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);

        await InfrastructureMigrationRunner.MigrateAsync(app.Services, cancellationToken);
    }

    private static void ConfigureDbContext(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'database' is required.");
        }

        if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(XtreamForgeDbContext).Assembly.FullName));

            return;
        }

        options.UseSqlite(connectionString);
    }
}
