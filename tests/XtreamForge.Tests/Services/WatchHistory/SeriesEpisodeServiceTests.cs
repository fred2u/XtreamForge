using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.Database;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.WatchHistory;

public class SeriesEpisodeServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SteppingTimeProvider _time = new();

    public SeriesEpisodeServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public void ReadEpisodes_ReadsTheEpisodesGroupedBySeason()
    {
        var episodes = SeriesEpisodeService.ReadEpisodes(Parse("""
            { "episodes": {
                "1": [ { "id": "1001", "episode_num": 1, "season": 1 }, { "id": 1002, "episode_num": "2", "season": "1" } ],
                "2": [ { "id": "2001", "episode_num": 1 } ] } }
            """));

        Assert.Equal(
            [new XtreamEpisode("1001", 1, 1), new XtreamEpisode("1002", 1, 2), new XtreamEpisode("2001", 2, 1)],
            episodes);
    }

    [Fact]
    public void ReadEpisodes_ReadsTheSeasonsListedAsAnArray()
    {
        var episodes = SeriesEpisodeService.ReadEpisodes(Parse("""
            { "episodes": [ [ { "id": "1001", "episode_num": 1, "season": 1 } ], [ { "id": "2001" } ] ] }
            """));

        Assert.Equal([new XtreamEpisode("1001", 1, 1), new XtreamEpisode("2001", null, null)], episodes);
    }

    [Theory]
    [InlineData("""{ "episodes": [] }""")]
    [InlineData("""{ "episodes": {} }""")]
    [InlineData("""{ "info": {} }""")]
    [InlineData("""{ "episodes": "none" }""")]
    [InlineData("""{ "episodes": { "1": { "id": "1001" } } }""")]
    public void ReadEpisodes_WithoutEpisodeList_ReturnsNothing(string payload)
    {
        Assert.Empty(SeriesEpisodeService.ReadEpisodes(Parse(payload)));
    }

    [Fact]
    public void ReadEpisodes_IgnoresTheEntriesWithoutValidIdAndTheDuplicates()
    {
        var episodes = SeriesEpisodeService.ReadEpisodes(Parse("""
            { "episodes": { "1": [
                { "episode_num": 1 }, { "id": "" }, { "id": "abc" }, { "id": "-3" }, "1004",
                { "id": "1001", "episode_num": "x", "season": -1 }, { "id": "1001", "episode_num": 9 } ] } }
            """));

        // an invalid season or number is unknown; the season of the group is used instead
        Assert.Equal([new XtreamEpisode("1001", 1, null)], episodes);
    }

    [Fact]
    public async Task SaveAsync_PersistsTheNewEpisodes()
    {
        var sourceId = await CreateSourceAsync();

        var saved = await CreateService().SaveAsync(
            new SeriesEpisodeRequest(sourceId, "42", [new XtreamEpisode("1001", 1, 1), new XtreamEpisode("1002", null, null)]),
            TestContext.Current.CancellationToken);

        Assert.True(saved);
        var episodes = await _dbContext.SeriesEpisodes.AsNoTracking().OrderBy(episode => episode.EpisodeId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [(sourceId, "1001", "42", (int?)1, (int?)1), (sourceId, "1002", "42", null, null)],
            episodes.Select(episode => (episode.XtreamSourceId, episode.EpisodeId, episode.SeriesId, episode.SeasonNumber, episode.EpisodeNumber)));
        Assert.All(episodes, episode => Assert.Equal(_time.Now, episode.UpdatedAtUtc));
    }

    [Fact]
    public async Task SaveAsync_UpdatesOnlyTheChangedEpisodes()
    {
        var sourceId = await CreateSourceAsync();
        var createdAt = _time.Now;
        await AddEpisodeAsync(sourceId, "1001", "42", 1, 1);
        await AddEpisodeAsync(sourceId, "1002", "42", 1, 2);
        _time.Now += TimeSpan.FromDays(1);

        var saved = await CreateService().SaveAsync(
            new SeriesEpisodeRequest(sourceId, "42", [new XtreamEpisode("1001", 1, 1), new XtreamEpisode("1002", 2, 1)]),
            TestContext.Current.CancellationToken);

        Assert.True(saved);
        var episodes = await _dbContext.SeriesEpisodes.AsNoTracking().OrderBy(episode => episode.EpisodeId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [("1001", (int?)1, (int?)1, createdAt), ("1002", 2, 1, _time.Now)],
            episodes.Select(episode => (episode.EpisodeId, episode.SeasonNumber, episode.EpisodeNumber, episode.UpdatedAtUtc)));
    }

    [Fact]
    public async Task SaveAsync_WhenNothingChanged_ReturnsFalse()
    {
        var sourceId = await CreateSourceAsync();
        await AddEpisodeAsync(sourceId, "1001", "42", 1, 1);

        var saved = await CreateService().SaveAsync(
            new SeriesEpisodeRequest(sourceId, "42", [new XtreamEpisode("1001", 1, 1)]),
            TestContext.Current.CancellationToken);

        Assert.False(saved);
    }

    [Fact]
    public async Task SaveAsync_KeepsTheEpisodesOfAnotherSourceApart()
    {
        var sourceId = await CreateSourceAsync();
        var otherSourceId = await CreateSourceAsync("other.example.com");
        await AddEpisodeAsync(otherSourceId, "1001", "77", 3, 3);

        await CreateService().SaveAsync(new SeriesEpisodeRequest(sourceId, "42", [new XtreamEpisode("1001", 1, 1)]), TestContext.Current.CancellationToken);

        var episodes = await _dbContext.SeriesEpisodes.AsNoTracking().OrderBy(episode => episode.XtreamSourceId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [(sourceId, "42"), (otherSourceId, "77")],
            episodes.Select(episode => (episode.XtreamSourceId, episode.SeriesId)));
    }

    private static JsonObject Parse(string payload) => Assert.IsType<JsonObject>(JsonNode.Parse(payload));

    private SeriesEpisodeService CreateService() => new(_dbContext, _time);

    private async Task<int> CreateSourceAsync(string host = "provider.example.com")
    {
        var source = new XtreamSource { Protocol = "http", Host = host, Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source.Id;
    }

    private async Task AddEpisodeAsync(int xtreamSourceId, string episodeId, string seriesId, int? seasonNumber, int? episodeNumber)
    {
        _dbContext.SeriesEpisodes.Add(new SeriesEpisode
        {
            XtreamSourceId = xtreamSourceId,
            EpisodeId = episodeId,
            SeriesId = seriesId,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            UpdatedAtUtc = _time.Now
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
