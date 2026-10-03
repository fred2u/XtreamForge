using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class WatchHistoryEndpointsTests : IAsyncDisposable
{
    private static readonly IOptions<TmdbOptions> TmdbOptions = Options.Create(new TmdbOptions { ImageBaseUrl = "https://image.tmdb.org/t/p/" });

    private readonly XtreamForgeDbContext _dbContext;
    private readonly WatchHistoryGetEndpoint _endpoint;

    public WatchHistoryEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _endpoint = new WatchHistoryGetEndpoint(new WatchHistoryAdminService(_dbContext), TmdbOptions);
    }

    [Fact]
    public async Task Get_ReturnsThePageWithThePosterThumbnailUrl()
    {
        var startedAtUtc = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
        _dbContext.TmdbInfos.Add(new TmdbInfo { ContentType = ContentType.Vod, TmdbId = 603, Title = "The Matrix", PosterPath = "/matrix.jpg" });
        _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = startedAtUtc });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _endpoint.GetAsync(new WatchHistoryListQuery(), TestContext.Current.CancellationToken);

        var page = Assert.IsType<Ok<AdminWatchHistoryPageDto>>(result).Value;
        Assert.NotNull(page);
        var entry = Assert.Single(page.Items);
        Assert.Equal(
            (603L, ContentType.Vod, startedAtUtc, "The Matrix", "https://image.tmdb.org/t/p/w92/matrix.jpg"),
            (entry.TmdbId, entry.ContentType, entry.StartedAtUtc, entry.Title, entry.PosterThumbnailUrl));
        Assert.Equal(1, page.MatchingCount);
    }

    [Theory]
    [InlineData(ContentType.Undefined)]
    [InlineData((ContentType)99)]
    public async Task Get_WithInvalidContentType_ReturnsValidationProblem(ContentType contentType)
    {
        var result = await _endpoint.GetAsync(new WatchHistoryListQuery(contentType), TestContext.Current.CancellationToken);

        Assert.Contains(nameof(WatchHistoryListQuery.ContentType), Assert.IsType<ValidationProblem>(result).ProblemDetails.Errors.Keys);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
