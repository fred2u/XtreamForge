using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Api.Services;

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
                ["Database:ApplyMigrations"] = "false"
            });
        });
    }

    public WebApplicationFactory<Program> WithForwarderHandler(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(ForwarderService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });
    }
}
