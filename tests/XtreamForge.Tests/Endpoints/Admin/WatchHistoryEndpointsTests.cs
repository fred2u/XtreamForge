using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.VirtualCategories;
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
    private readonly TmdbIdCache _recommendationCache = new(TimeProvider.System);
    private readonly WatchHistoryAdminService _service;

    public WatchHistoryEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new WatchHistoryAdminService(_dbContext, _recommendationCache, TimeProvider.System);
    }

    [Fact]
    public async Task GetActivity_WithUnknownTimeZone_ReturnsValidationProblem()
    {
        var result = await WatchHistoryEndpoints.GetActivityAsync("Mars/Olympus_Mons", _service, TestContext.Current.CancellationToken);

        Assert.Contains("timeZone", Assert.IsType<ValidationProblem>(result).ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task GetActivity_WithoutTimeZone_UsesUtc()
    {
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await WatchHistoryEndpoints.GetActivityAsync(null, _service, TestContext.Current.CancellationToken);
        var after = DateOnly.FromDateTime(DateTime.UtcNow);

        var activity = Assert.IsType<Ok<WatchHistoryActivity>>(result).Value;
        Assert.NotNull(activity);
        Assert.InRange(activity.To, before, after);
    }

    [Fact]
    public async Task Post_ReturnsTheRecordedPlaybackWithThePosterThumbnailUrl()
    {
        var info = new TmdbInfo { ContentType = ContentType.Vod, TmdbId = 603, Title = "The Lattice", PosterPath = "/lattice.jpg" };
        _dbContext.TmdbInfos.Add(info);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await WatchHistoryEndpoints.PostAsync(info.Id, _service, TmdbOptions, TestContext.Current.CancellationToken);

        var entry = Assert.IsType<Ok<AdminWatchHistoryEntryDto>>(result).Value;
        Assert.NotNull(entry);
        Assert.Equal((603L, "https://image.tmdb.org/t/p/w92/lattice.jpg"), (entry.TmdbId, entry.PosterThumbnailUrl));
    }

    [Fact]
    public async Task Post_WithUnknownTmdbInfo_ReturnsNotFound()
    {
        Assert.IsType<NotFound>(await WatchHistoryEndpoints.PostAsync(42, _service, TmdbOptions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_ReturnsNoContentThenNotFound()
    {
        var entry = new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = DateTimeOffset.UtcNow };
        _dbContext.WatchHistory.Add(entry);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(await WatchHistoryEndpoints.DeleteAsync(entry.Id, _service, TestContext.Current.CancellationToken));
        Assert.IsType<NotFound>(await WatchHistoryEndpoints.DeleteAsync(entry.Id, _service, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_ReturnsThePageWithThePosterThumbnailUrl()
    {
        var startedAtUtc = new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
        _dbContext.TmdbInfos.Add(new TmdbInfo { ContentType = ContentType.Vod, TmdbId = 603, Title = "The Lattice", PosterPath = "/lattice.jpg" });
        _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = startedAtUtc });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await WatchHistoryEndpoints.GetListAsync(new WatchHistoryListQuery(), _service, TmdbOptions, TestContext.Current.CancellationToken);

        var page = Assert.IsType<Ok<AdminWatchHistoryPageDto>>(result).Value;
        Assert.NotNull(page);
        var entry = Assert.Single(page.Items);
        Assert.Equal(
            (603L, ContentType.Vod, startedAtUtc, "The Lattice", "https://image.tmdb.org/t/p/w92/lattice.jpg"),
            (entry.TmdbId, entry.ContentType, entry.StartedAtUtc, entry.Title, entry.PosterThumbnailUrl));
        Assert.Equal(1, page.MatchingCount);
    }

    [Fact]
    public async Task Get_ReturnsTheSeasonAndEpisodeOfAnEpisodePlayback()
    {
        _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Series, TmdbId = 1399, SeasonNumber = 1, EpisodeNumber = 3, StartedAtUtc = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await WatchHistoryEndpoints.GetListAsync(new WatchHistoryListQuery(ContentType.Series), _service, TmdbOptions, TestContext.Current.CancellationToken);

        var page = Assert.IsType<Ok<AdminWatchHistoryPageDto>>(result).Value;
        Assert.NotNull(page);
        var entry = Assert.Single(page.Items);
        Assert.Equal((ContentType.Series, 1399L, (int?)1, (int?)3), (entry.ContentType, entry.TmdbId, entry.SeasonNumber, entry.EpisodeNumber));
    }

    [Theory]
    [InlineData(ContentType.Undefined)]
    [InlineData((ContentType)99)]
    public async Task Get_WithInvalidContentType_ReturnsValidationProblem(ContentType contentType)
    {
        var result = await WatchHistoryEndpoints.GetListAsync(new WatchHistoryListQuery(contentType), _service, TmdbOptions, TestContext.Current.CancellationToken);

        Assert.Contains(nameof(WatchHistoryListQuery.ContentType), Assert.IsType<ValidationProblem>(result).ProblemDetails.Errors.Keys);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _recommendationCache.Dispose();
        GC.SuppressFinalize(this);
    }
}
