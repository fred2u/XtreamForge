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
    private readonly WatchHistoryAdminService _service;

    public WatchHistoryAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new WatchHistoryAdminService(_dbContext);
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
