using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class WatchHistoryAdminServiceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    private readonly XtreamForgeDbContext _dbContext;
    private readonly SteppingTimeProvider _time = new() { Now = Day.AddDays(1) };
    private readonly WatchHistoryAdminService _service;

    public WatchHistoryAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new WatchHistoryAdminService(_dbContext, _time);
    }

    [Fact]
    public async Task GetActivityAsync_CountsTheMoviePlaybacksPerDayOfTheTimeZone()
    {
        // now: 2026-10-02 22:00 in Brussels (UTC+2)
        var brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        _dbContext.WatchHistory.AddRange(
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 1, StartedAtUtc = new DateTimeOffset(2026, 10, 1, 21, 0, 0, TimeSpan.Zero) },
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 2, StartedAtUtc = new DateTimeOffset(2026, 10, 1, 23, 30, 0, TimeSpan.Zero) },
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 3, StartedAtUtc = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero) },
            new WatchHistoryEntry { ContentType = ContentType.Series, TmdbId = 4, StartedAtUtc = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero) },
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 5, StartedAtUtc = new DateTimeOffset(2025, 9, 1, 12, 0, 0, TimeSpan.Zero) });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var activity = await _service.GetActivityAsync(brussels, TestContext.Current.CancellationToken);

        Assert.Equal((new DateOnly(2025, 9, 27), new DateOnly(2026, 10, 2)), (activity.From, activity.To));
        Assert.Equal(
            [new WatchHistoryDayActivity(new DateOnly(2026, 10, 1), 1), new WatchHistoryDayActivity(new DateOnly(2026, 10, 2), 2)],
            activity.Days);
    }

    [Fact]
    public async Task AddAsync_RecordsAPlaybackOfTheTmdbInfoStartedNow()
    {
        var info = new TmdbInfo { ContentType = ContentType.Vod, TmdbId = 603, Title = "The Matrix", PosterPath = "/matrix.jpg" };
        _dbContext.TmdbInfos.Add(info);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var item = await _service.AddAsync(info.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(item);
        Assert.Equal((ContentType.Vod, 603L, _time.Now, "The Matrix", "/matrix.jpg"), (item.ContentType, item.TmdbId, item.StartedAtUtc, item.Title, item.PosterPath));
        var entry = Assert.Single(_dbContext.WatchHistory);
        Assert.Equal((item.Id, ContentType.Vod, 603L, _time.Now), (entry.Id, entry.ContentType, entry.TmdbId, entry.StartedAtUtc));
    }

    [Fact]
    public async Task AddAsync_WithUnknownTmdbInfo_ReturnsNullAndRecordsNothing()
    {
        var item = await _service.AddAsync(42, TestContext.Current.CancellationToken);

        Assert.Null(item);
        Assert.Empty(_dbContext.WatchHistory);
    }

    [Fact]
    public async Task DeleteAsync_DeletesOnlyThePlayback()
    {
        var deleted = new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = Day };
        var kept = new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = Day.AddHours(1) };
        _dbContext.WatchHistory.AddRange(deleted, kept);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.DeleteAsync(deleted.Id, TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.Equal([kept.Id], _dbContext.WatchHistory.AsNoTracking().Select(entry => entry.Id));
    }

    [Fact]
    public async Task DeleteAsync_WithUnknownPlayback_ReturnsFalse()
    {
        Assert.False(await _service.DeleteAsync(42, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetPageAsync_ReturnsThePlaybacksMostRecentFirstWithTheirTmdbInfo()
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new WatchHistoryListQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(
            [(603L, Day.AddDays(2), "The Matrix"), (604L, Day.AddDays(1), null), (603L, Day, "The Matrix")],
            page.Items.Select(item => (item.TmdbId, item.StartedAtUtc, item.Title)));
        Assert.Equal(3, page.MatchingCount);
        Assert.Equal(("/matrix.jpg", new DateOnly(1999, 3, 31)), (page.Items[0].PosterPath, page.Items[0].ReleaseDate));
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetPageAsync_DoesNotJoinTheTmdbInfoOfAnotherContentType()
    {
        _dbContext.TmdbInfos.Add(new TmdbInfo { ContentType = ContentType.Series, TmdbId = 603, Title = "A TV show" });
        _dbContext.WatchHistory.Add(new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = Day });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var page = await _service.GetPageAsync(new WatchHistoryListQuery(), TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(page.Items).Title);
    }

    [Fact]
    public async Task GetPageAsync_PagesTheHistoryAndCountsEveryEntry()
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new WatchHistoryListQuery(Skip: 1, Take: 1), TestContext.Current.CancellationToken);

        Assert.Equal(604, Assert.Single(page.Items).TmdbId);
        Assert.Equal(3, page.MatchingCount);
    }

    [Theory]
    [InlineData(ContentType.Vod, 3)]
    [InlineData(ContentType.Series, 0)]
    public async Task GetPageAsync_FiltersByContentType(ContentType contentType, int expectedCount)
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new WatchHistoryListQuery(contentType), TestContext.Current.CancellationToken);

        Assert.Equal((expectedCount, expectedCount), (page.Items.Count, page.MatchingCount));
    }

    private async Task SeedAsync()
    {
        _dbContext.TmdbInfos.Add(new TmdbInfo
        {
            ContentType = ContentType.Vod,
            TmdbId = 603,
            Title = "The Matrix",
            ReleaseDate = new DateOnly(1999, 3, 31),
            PosterPath = "/matrix.jpg"
        });
        _dbContext.WatchHistory.AddRange(
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = Day },
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 604, StartedAtUtc = Day.AddDays(1) },
            new WatchHistoryEntry { ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = Day.AddDays(2) });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
