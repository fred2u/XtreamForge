using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.VirtualCategories;

public sealed class PopularServiceTests : IDisposable
{
    private readonly TmdbIdCache _cache = new(TimeProvider.System);

    [Theory]
    [InlineData(ContentType.Vod, "movie/popular")]
    [InlineData(ContentType.Series, "tv/popular")]
    public async Task GetPopularTmdbIds_ReadsTheFirstPagesOnceAndCachesThem(ContentType contentType, string path)
    {
        var tmdb = new StubTmdbHttpClientFactory(uri =>
        {
            var page = uri.Query.TrimStart('?').Split('&').Single(parameter => parameter.StartsWith("page=", StringComparison.Ordinal))[5..];
            return StubTmdbHttpClientFactory.GetRelativePath(uri) == path ? $$"""{ "results": [ { "id": {{page}} }, { "id": 100 } ] }""" : null;
        });
        var service = tmdb.CreatePopularService(_cache);

        var first = await service.GetPopularTmdbIdsAsync(contentType, TestContext.Current.CancellationToken);
        var second = await service.GetPopularTmdbIdsAsync(contentType, TestContext.Current.CancellationToken);

        Assert.Equal([1L, 2L, 3L, 4L, 5L, 100L], first.Order());
        Assert.Same(first, second);
        Assert.Equal(PopularService.PageCount, tmdb.RequestedUris.Count);
    }

    [Fact]
    public async Task GetPopularTmdbIds_WithoutApiKey_DoesNotCallTmdb()
    {
        var tmdb = new StubTmdbHttpClientFactory(_ => """{ "results": [ { "id": 1 } ] }""");

        var popular = await tmdb.CreatePopularService(_cache, apiKey: "").GetPopularTmdbIdsAsync(ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Empty(popular);
        Assert.Empty(tmdb.RequestedUris);
    }

    [Fact]
    public async Task GetPopularTmdbIds_WhenTmdbFails_ReturnsNothingAndRetriesAfterTheFailureDuration()
    {
        var failing = true;
        var tmdb = new StubTmdbHttpClientFactory(_ => failing ? throw new HttpRequestException("TMDB is down") : """{ "results": [ { "id": 1 } ] }""");
        var time = new SteppingTimeProvider();
        using var cache = new TmdbIdCache(time);
        var service = tmdb.CreatePopularService(cache);

        Assert.Empty(await service.GetPopularTmdbIdsAsync(ContentType.Vod, TestContext.Current.CancellationToken));

        failing = false;
        var failedRequestCount = tmdb.RequestedUris.Count;

        // TMDB is not called again, nor waited for, before the failure duration
        Assert.Empty(await service.GetPopularTmdbIdsAsync(ContentType.Vod, TestContext.Current.CancellationToken));
        Assert.Equal(failedRequestCount, tmdb.RequestedUris.Count);

        time.Now += TmdbIdCache.FailureDuration;
        Assert.Equal([1L], await service.GetPopularTmdbIdsAsync(ContentType.Vod, TestContext.Current.CancellationToken));
    }

    public void Dispose() => _cache.Dispose();
}
