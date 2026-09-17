using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Categories;
using XtreamForge.Data;
using XtreamForge.Source;

namespace XtreamForge.Tests;

public sealed class SourceServiceTests
{
    [Fact]
    public async Task SynchronizeCategoriesAsync_CreatesSource_AndGetSourceIdAsyncReturnsIt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var sourceService = serviceProvider.GetRequiredService<SourceService>();
        var descriptor = new XtreamSourceDescriptor("https", "example.com", 443);

        var result = await sourceService.SynchronizeCategoriesAsync(
            descriptor,
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta")
            ]);

        var sourceId = await sourceService.GetSourceIdAsync(descriptor);
        var sources = await sourceService.GetSourcesAsync();

        Assert.Equal(sourceId, result.SourceId);
        Assert.True(await sourceService.SourceExistsAsync(result.SourceId));
        var source = Assert.Single(sources);
        Assert.Equal((result.SourceId, "https", "example.com", 443), (source.Id, source.Protocol, source.Host, source.Port));
    }

    [Fact]
    public async Task SynchronizeCategoriesAsync_PreservesStableDedicatedOutputIdentitiesAcrossRefreshes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var sourceService = serviceProvider.GetRequiredService<SourceService>();
        var descriptor = new XtreamSourceDescriptor("https", "example.com", 443);

        var first = await sourceService.SynchronizeCategoriesAsync(
            descriptor,
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta")
            ]);

        var second = await sourceService.SynchronizeCategoriesAsync(
            descriptor,
            ContentType.Vod,
            [
                new DiscoveredCategory("57", "Beta"),
                new DiscoveredCategory("81", "Gamma"),
                new DiscoveredCategory("42", "Alpha")
            ]);

        var firstByUpstreamId = first.Categories.ToDictionary(category => category.UpstreamCategoryId, StringComparer.Ordinal);
        var secondByUpstreamId = second.Categories.ToDictionary(category => category.UpstreamCategoryId, StringComparer.Ordinal);

        Assert.Equal(firstByUpstreamId["42"].DedicatedXtreamForgeCategoryId, secondByUpstreamId["42"].DedicatedXtreamForgeCategoryId);
        Assert.Equal(firstByUpstreamId["57"].DedicatedXtreamForgeCategoryId, secondByUpstreamId["57"].DedicatedXtreamForgeCategoryId);
        Assert.Equal(3, secondByUpstreamId["81"].DedicatedXtreamForgeCategoryId);
    }

    [Fact]
    public async Task SynchronizeCategoriesAsync_WarmRefreshUpdatesDiscoveryTimestamps_WithoutPerCategoryTrackedWrites()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var commandCounter = new DbCommandCounterInterceptor();
        var saveChangesCounter = new SaveChangesCounterInterceptor();
        await using var serviceProvider = CreateServiceProvider(connection, commandCounter, saveChangesCounter);
        await EnsureCreatedAsync(serviceProvider);

        var sourceService = serviceProvider.GetRequiredService<SourceService>();
        var descriptor = new XtreamSourceDescriptor("https", "example.com", 443);

        await sourceService.SynchronizeCategoriesAsync(
            descriptor,
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta")
            ]);

        var sourceId = (await sourceService.GetSourceIdAsync(descriptor))!.Value;
        var firstCategories = await sourceService.GetSourceCategoriesAsync(sourceId, ContentType.Vod);
        var firstLastDiscoveredAtUtc = firstCategories.ToDictionary(category => category.UpstreamCategoryId, category => category.LastDiscoveredAtUtc, StringComparer.Ordinal);

        await Task.Delay(25);
        commandCounter.Reset();
        saveChangesCounter.Reset();

        await sourceService.SynchronizeCategoriesAsync(
            descriptor,
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta")
            ]);

        var refreshedCategories = await sourceService.GetSourceCategoriesAsync(sourceId, ContentType.Vod);

        Assert.All(
            refreshedCategories,
            category => Assert.True(category.LastDiscoveredAtUtc > firstLastDiscoveredAtUtc[category.UpstreamCategoryId]));
        Assert.InRange(commandCounter.CommandCount, 1, 5);
        Assert.InRange(commandCounter.WriteCommandCount, 1, 2);
        Assert.InRange(saveChangesCounter.SaveChangesCount, 1, 1);
    }

    [Fact]
    public async Task GetSourceCategoriesAsync_ByDescriptor_CanRestrictToRequestedUpstreamIds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var serviceProvider = CreateServiceProvider(connection);
        await EnsureCreatedAsync(serviceProvider);

        var sourceService = serviceProvider.GetRequiredService<SourceService>();
        var descriptor = new XtreamSourceDescriptor("https", "example.com", 443);

        await sourceService.SynchronizeCategoriesAsync(
            descriptor,
            ContentType.Vod,
            [
                new DiscoveredCategory("42", "Alpha"),
                new DiscoveredCategory("57", "Beta"),
                new DiscoveredCategory("81", "Gamma")
            ]);

        var categories = await sourceService.GetSourceCategoriesAsync(
            1,
            ContentType.Vod,
            ["57", "81"]);

        Assert.Equal(["57", "81"], [.. categories.Select(category => category.UpstreamCategoryId)]);
    }

    private static ServiceProvider CreateServiceProvider(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<XtreamForgeDbContext>(options =>
        {
            options.UseSqlite(connection);
            if (interceptors.Length > 0)
            {
                options.AddInterceptors(interceptors);
            }
        });
        services.AddScoped(static provider => provider.GetRequiredService<IDbContextFactory<XtreamForgeDbContext>>().CreateDbContext());
        services.AddSingleton<SourceService>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureCreatedAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }
}
