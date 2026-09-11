using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using XtreamForge.Api.Services;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Api.Tests;

public sealed class XtreamForgeApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:database"] = "Host=localhost;Port=5432;Database=xtreamforge_tests;Username=test;******",
                ["Database:ApplyMigrations"] = "false",
                ["XtreamProxy:AllowedHosts:0"] = "example.com",
                ["XtreamProxy:AllowedHosts:1"] = "127.0.0.1",
                ["XtreamProxy:AllowedHosts:2"] = "localhost"
            });
        });
    }
}

internal static class XtreamForgeApiFactoryExtensions
{
    internal static WebApplicationFactory<Program> WithForwarderHandler(
        this WebApplicationFactory<Program> factory,
        HttpMessageHandler handler,
        TestLogSink? logSink = null,
        bool failIfDatabaseAccessed = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(handler);

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(ForwarderService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);

                if (failIfDatabaseAccessed)
                {
                    services.RemoveAll<IDbContextFactory<XtreamForgeDbContext>>();
                    services.RemoveAll<XtreamForgeDbContext>();
                    services.RemoveAll<DbContextOptions<XtreamForgeDbContext>>();
                    services.AddSingleton<IDbContextFactory<XtreamForgeDbContext>, ThrowingDbContextFactory>();
                    services.AddScoped(static serviceProvider =>
                        serviceProvider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
                }
            });

            if (logSink is not null)
            {
                builder.ConfigureLogging(logging =>
                {
                    logging.AddProvider(new TestLoggerProvider(logSink));
                });
            }
        });
    }

    internal static WebApplicationFactory<Program> WithFailingDatabaseFactory(
        this WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextFactory<XtreamForgeDbContext>>();
                services.RemoveAll<XtreamForgeDbContext>();
                services.RemoveAll<DbContextOptions<XtreamForgeDbContext>>();
                services.AddSingleton<IDbContextFactory<XtreamForgeDbContext>, ThrowingDbContextFactory>();
                services.AddScoped(static serviceProvider =>
                    serviceProvider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
            });
        });

    internal static WebApplicationFactory<Program> WithSqliteDatabase(
        this WebApplicationFactory<Program> factory,
        string? databasePath = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            var effectiveDatabasePath = databasePath ?? Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = effectiveDatabasePath
            }.ToString();

            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:database"] = connectionString
                });
            });
        });
}

internal sealed class ThrowingDbContextFactory : IDbContextFactory<XtreamForgeDbContext>
{
    public XtreamForgeDbContext CreateDbContext() =>
        throw new InvalidOperationException("Database access is not expected during proxy requests.");

    public Task<XtreamForgeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Database access is not expected during proxy requests.");
}
