using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

    internal WebApplicationFactory<Program> WithForwarderHandler(
        HttpMessageHandler handler,
        TestLogSink? logSink = null,
        bool failIfDatabaseAccessed = false)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(ForwarderService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);

                if (failIfDatabaseAccessed)
                {
                    services.RemoveAll<IDbContextFactory<XtreamForgeDbContext>>();
                    services.RemoveAll<XtreamForgeDbContext>();
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

    internal WebApplicationFactory<Program> WithFailingDatabaseFactory() =>
        WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDbContextFactory<XtreamForgeDbContext>>();
                services.RemoveAll<XtreamForgeDbContext>();
                services.AddSingleton<IDbContextFactory<XtreamForgeDbContext>, ThrowingDbContextFactory>();
                services.AddScoped(static serviceProvider =>
                    serviceProvider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
            });
        });

    private sealed class ThrowingDbContextFactory : IDbContextFactory<XtreamForgeDbContext>
    {
        public XtreamForgeDbContext CreateDbContext() =>
            throw new InvalidOperationException("Database access is not expected during proxy requests.");

        public Task<XtreamForgeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database access is not expected during proxy requests.");
    }
}
