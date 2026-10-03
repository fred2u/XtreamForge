using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class StreamTmdbMappingAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbInfoQueue _tmdbInfoQueue = new();
    private readonly StreamTmdbMappingAdminService _service;

    public StreamTmdbMappingAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new StreamTmdbMappingAdminService(_dbContext, _tmdbInfoQueue);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsTheMappingsOfTheSourceAndContentTypeMostRecentFirstWithTheirTmdbInfo()
    {
        var sourceId = await SeedAsync();

        var page = await _service.GetPageAsync(sourceId, new StreamTmdbMappingListQuery(ContentType.Vod), TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(
            [("3", null, null), ("2", 604L, null), ("1", (long?)603L, "The Matrix")],
            page.Items.Select(item => (item.StreamId, item.TmdbId, item.Title)));
        Assert.Equal((3, 3, 2), (page.MatchingCount, page.TotalCount, page.MappedCount));
        Assert.Equal(("/matrix.jpg", new DateOnly(1999, 3, 31)), (page.Items[^1].PosterPath, page.Items[^1].ReleaseDate));
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(true, new[] { "2", "1" })]
    [InlineData(false, new[] { "3" })]
    public async Task GetPageAsync_FiltersByMappedState(bool isMapped, string[] expectedStreamIds)
    {
        var sourceId = await SeedAsync();

        var page = await _service.GetPageAsync(sourceId, new StreamTmdbMappingListQuery(ContentType.Vod, IsMapped: isMapped), TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(expectedStreamIds, page.Items.Select(item => item.StreamId));
        Assert.Equal((expectedStreamIds.Length, 3), (page.MatchingCount, page.TotalCount));
    }

    [Theory]
    [InlineData("3", new[] { "3" })]
    [InlineData("604", new[] { "2" })]
    [InlineData(" mATRIX ", new[] { "1" })]
    [InlineData("matrice", new[] { "1" })]
    [InlineData("60", new string[0])]
    public async Task GetPageAsync_SearchMatchesExactStreamIdExactTmdbIdOrTitles(string search, string[] expectedStreamIds)
    {
        var sourceId = await SeedAsync();

        var page = await _service.GetPageAsync(sourceId, new StreamTmdbMappingListQuery(ContentType.Vod, Search: search), TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(expectedStreamIds, page.Items.Select(item => item.StreamId));
        Assert.Equal(expectedStreamIds.Length, page.MatchingCount);
    }

    [Fact]
    public async Task GetPageAsync_ClampsThePage()
    {
        var sourceId = await SeedAsync();

        var page = await _service.GetPageAsync(sourceId, new StreamTmdbMappingListQuery(ContentType.Vod, Skip: 1, Take: 1), TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.Equal(["2"], page.Items.Select(item => item.StreamId));
        Assert.Equal(3, page.MatchingCount);
    }

    [Fact]
    public async Task GetPageAsync_WhenSourceDoesNotExist_ReturnsNull()
    {
        var page = await _service.GetPageAsync(999, new StreamTmdbMappingListQuery(ContentType.Vod), TestContext.Current.CancellationToken);

        Assert.Null(page);
    }

    [Fact]
    public async Task SetTmdbIdAsync_MapsAnUnmappedStreamAndEnqueuesItsTmdbInfo()
    {
        var sourceId = await SeedAsync();
        var unmapped = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(mapping => mapping.XtreamSourceId == sourceId && mapping.StreamId == "3", TestContext.Current.CancellationToken);

        var found = await _service.SetTmdbIdAsync(unmapped.Id, 680, TestContext.Current.CancellationToken);

        Assert.True(found);
        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(mapping => mapping.Id == unmapped.Id, TestContext.Current.CancellationToken);
        Assert.Equal((680L, (DateTimeOffset?)null, 2), (mapping.TmdbId, mapping.NextLookupAtUtc, mapping.LookupAttemptCount));
        Assert.Equal(1, _tmdbInfoQueue.Count);
        Assert.False(_tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(ContentType.Vod, 680)));
    }

    [Fact]
    public async Task SetTmdbIdAsync_CorrectsAMappedStream()
    {
        var sourceId = await SeedAsync();
        var mapped = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(mapping => mapping.XtreamSourceId == sourceId && mapping.StreamId == "1", TestContext.Current.CancellationToken);

        var found = await _service.SetTmdbIdAsync(mapped.Id, 604, TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.Equal(604, (await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(mapping => mapping.Id == mapped.Id, TestContext.Current.CancellationToken)).TmdbId);
    }

    [Fact]
    public async Task SetTmdbIdAsync_WhenMappingDoesNotExist_ReturnsFalse()
    {
        var found = await _service.SetTmdbIdAsync(999, 603, TestContext.Current.CancellationToken);

        Assert.False(found);
        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    // VOD mappings "1" (mapped, metadata loaded), "2" (mapped, no metadata), and "3" (not found yet) of a source,
    // and mappings that must not be listed: another content type and another source
    private async Task<int> SeedAsync()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        var otherSource = new XtreamSource { Protocol = "http", Host = "other.example.com", Port = 8080 };
        _dbContext.XtreamSources.AddRange(source, otherSource);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.StreamTmdbMappings.AddRange(
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 },
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Vod, StreamId = "2", TmdbId = 604 },
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Vod, StreamId = "3", LookupAttemptCount = 2, NextLookupAtUtc = DateTimeOffset.UtcNow.AddDays(2) },
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Series, StreamId = "4", TmdbId = 603 },
            new StreamTmdbMapping { XtreamSourceId = otherSource.Id, ContentType = ContentType.Vod, StreamId = "5", TmdbId = 603 });
        _dbContext.TmdbInfos.AddRange(
            new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "The Matrix", OriginalTitle = "Matrice", ReleaseDate = new DateOnly(1999, 3, 31), PosterPath = "/matrix.jpg", NextLoadAtUtc = DateTimeOffset.UtcNow },
            // same TMDB ID for another content type: must not be joined to the VOD mappings
            new TmdbInfo { TmdbId = 604, ContentType = ContentType.Series, Title = "A show", NextLoadAtUtc = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return source.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
