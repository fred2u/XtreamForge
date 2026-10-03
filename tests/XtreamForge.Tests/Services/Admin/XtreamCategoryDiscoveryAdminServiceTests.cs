using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class XtreamCategoryDiscoveryAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;

    public XtreamCategoryDiscoveryAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task DiscoverAsync_WhenUpstreamSucceeds_PersistsVodAndSeriesCategories()
    {
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_categories"] = (HttpStatusCode.OK, """[{"category_id":"1","category_name":"Action"},{"category_id":"2","category_name":"Comedy"}]"""),
            ["get_series_categories"] = (HttpStatusCode.OK, """[{"category_id":"10","category_name":"Drama"}]""")
        });
        var service = CreateService(httpClientFactory);

        var source = await service.DiscoverAsync("http", "provider.example.com", 8080, "user", "secret", TestContext.Current.CancellationToken);

        Assert.NotNull(source);
        var categories = await _dbContext.XtreamCategories.AsNoTracking()
            .Where(c => c.XtreamSourceId == source.Id)
            .OrderBy(c => c.XtreamId)
            .Select(c => new { c.XtreamId, c.Name, c.ContentType })
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [new { XtreamId = "1", Name = "Action", ContentType = ContentType.Vod },
             new { XtreamId = "10", Name = "Drama", ContentType = ContentType.Series },
             new { XtreamId = "2", Name = "Comedy", ContentType = ContentType.Vod }],
            categories);
    }

    [Fact]
    public async Task DiscoverAsync_CallsPlayerApiWithCredentialsForEachContentType()
    {
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_categories"] = (HttpStatusCode.OK, "[]"),
            ["get_series_categories"] = (HttpStatusCode.OK, "[]")
        });
        var service = CreateService(httpClientFactory);

        await service.DiscoverAsync("https", "provider.example.com", 8443, "us er", "p&ss", TestContext.Current.CancellationToken);

        Assert.Equal(
            ["https://provider.example.com:8443/player_api.php?username=us%20er&password=p%26ss&action=get_vod_categories",
             "https://provider.example.com:8443/player_api.php?username=us%20er&password=p%26ss&action=get_series_categories"],
            httpClientFactory.RequestedUris.Select(u => u.AbsoluteUri));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "[]")]
    [InlineData(HttpStatusCode.OK, "not json")]
    public async Task DiscoverAsync_WhenUpstreamFails_ReturnsNullAndSavesNothing(HttpStatusCode statusCode, string content)
    {
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_categories"] = (HttpStatusCode.OK, """[{"category_id":"1","category_name":"Action"}]"""),
            ["get_series_categories"] = (statusCode, content)
        });
        var service = CreateService(httpClientFactory);

        var source = await service.DiscoverAsync("http", "provider.example.com", 8080, "user", "secret", TestContext.Current.CancellationToken);

        Assert.Null(source);
        Assert.False(await _dbContext.XtreamSources.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await _dbContext.XtreamCategories.AnyAsync(TestContext.Current.CancellationToken));
    }

    private XtreamCategoryDiscoveryAdminService CreateService(StubXtreamHttpClientFactory httpClientFactory) =>
        new(httpClientFactory, new CategoryService(_dbContext), NullLogger<XtreamCategoryDiscoveryAdminService>.Instance);

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
