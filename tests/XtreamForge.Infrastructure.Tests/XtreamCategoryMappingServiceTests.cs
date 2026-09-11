using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Infrastructure.Data;
using XtreamForge.Infrastructure.Models;
using XtreamForge.Infrastructure.Services;

namespace XtreamForge.Infrastructure.Tests;

public sealed class XtreamCategoryMappingServiceTests
{
    [Fact]
    public async Task SyncCategoriesAsync_CreatesStableSourceAndOutputMappings()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var service = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();

        var firstResult = await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta")
            ]);

        var secondResult = await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("57", "Beta"),
                new DiscoveredCategory("81", "Gamma"),
                new DiscoveredCategory("42", "Alpha")
            ]);

        Assert.Collection(
            firstResult,
            first => Assert.Equal(("1", "Alpha"), (first.CategoryId, first.CategoryName)),
            second => Assert.Equal(("2", "Beta"), (second.CategoryId, second.CategoryName)));

        Assert.Collection(
            secondResult,
            first => Assert.Equal(("1", "Alpha"), (first.CategoryId, first.CategoryName)),
            second => Assert.Equal(("2", "Beta"), (second.CategoryId, second.CategoryName)),
            third => Assert.Equal(("3", "Gamma"), (third.CategoryId, third.CategoryName)));

        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(1, await dbContext.XtreamSources.CountAsync());
        Assert.Equal(3, await dbContext.OutputCategories.CountAsync());
        Assert.Equal(3, await dbContext.UpstreamCategories.CountAsync());
    }

    [Fact]
    public async Task SaveCategoryConfigurationAsync_SupportsRenameMergeAndExclude()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var service = serviceProvider.GetRequiredService<XtreamCategoryMappingService>();

        await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "|FR| 4K ⁴ᴷ"),
                new DiscoveredCategory("57", "|FR| FILMS 4K UHD"),
                new DiscoveredCategory("94", "|xxx| Something")
            ]);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category42 = categories.Single(category => category.UpstreamCategoryId == "42");
            var category57 = categories.Single(category => category.UpstreamCategoryId == "57");
            var category94 = categories.Single(category => category.UpstreamCategoryId == "94");

            await service.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                category42.Id,
                category42.XtreamSourceId,
                ContentType.Vod,
                false,
                null,
                "|FR| FILMS 4K"));

            await service.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                category57.Id,
                category57.XtreamSourceId,
                ContentType.Vod,
                false,
                category42.DedicatedOutputCategoryId,
                "|FR| FILMS 4K"));

            await service.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                category94.Id,
                category94.XtreamSourceId,
                ContentType.Vod,
                true,
                null,
                null));
        }

        var result = await service.SyncCategoriesAsync(
            new XtreamSourceDescriptor("https", "example.com", 443),
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "|FR| 4K ⁴ᴷ"),
                new DiscoveredCategory("57", "|FR| FILMS 4K UHD"),
                new DiscoveredCategory("94", "|xxx| Something")
            ]);

        Assert.Single(result);
        Assert.Equal(("1", "|FR| FILMS 4K"), (result[0].CategoryId, result[0].CategoryName));
    }

    private static ServiceProvider CreateServiceProvider(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<XtreamForgeDbContext>(options => options.UseSqlite(connection));
        services.AddScoped<XtreamCategoryMappingService>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureCreatedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }
}
