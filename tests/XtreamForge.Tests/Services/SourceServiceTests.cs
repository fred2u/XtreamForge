using Microsoft.AspNetCore.Http;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services;

public class SourceServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SteppingTimeProvider _time = new();
    private readonly SourceService _service;

    public SourceServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new SourceService(_dbContext, _time);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenSourceIsUnknown_ReturnsNull()
    {
        var snapshot = await _service.GetSnapshotAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_WhenSourceIsKnown_LoadsSourceData()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.StreamTmdbMappings.Add(new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 });
        await SaveAsync(source);

        var snapshot = await _service.GetSnapshotAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(source.Id, snapshot.Id);
        Assert.Equal(603, snapshot.StreamTmdbMappings["1"]);
    }

    [Fact]
    public async Task GetSnapshotAsync_LoadsEnabledRulesOfRequestedContentTypeInSequenceOrder()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.CategoryRules.AddRange(
        [
            new CategoryRule { ContentType = ContentType.Vod, Sequence = 20, Pattern = "second" },
            new CategoryRule { ContentType = ContentType.Vod, Sequence = 10, Pattern = "first" },
            new CategoryRule { ContentType = ContentType.Vod, Sequence = 30, Pattern = "disabled", IsEnabled = false },
            new CategoryRule { ContentType = ContentType.Series, Sequence = 40, Pattern = "series" }
        ]);
        source.ItemRules.AddRange(
        [
            new ItemRule { ContentType = ContentType.Vod, Sequence = 20, Pattern = "second" },
            new ItemRule { ContentType = ContentType.Vod, Sequence = 10, Pattern = "first" },
            new ItemRule { ContentType = ContentType.Series, Sequence = 30, Pattern = "series" }
        ]);
        await SaveAsync(source);

        var snapshot = await _service.GetSnapshotAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(["first", "second"], snapshot.CategoryRules.Select(rule => rule.Pattern));
        Assert.Equal(["first", "second"], snapshot.ItemRules.Select(rule => rule.Pattern));
    }

    [Fact]
    public async Task GetSnapshotAsync_LoadsOnlyMappingsOfRequestedContentTypeWithoutTracking()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.StreamTmdbMappings.AddRange(
        [
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 },
            new StreamTmdbMapping { ContentType = ContentType.Series, StreamId = "2", TmdbId = 1399 }
        ]);
        await SaveAsync(source);

        var snapshot = await _service.GetSnapshotAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(["1"], snapshot.StreamTmdbMappings.Keys);
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Fact]
    public async Task GetSnapshotAsync_ExposesOnlyNotYetDueLookupsAsDeferred()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.StreamTmdbMappings.AddRange(
        [
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 },
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "2", LookupAttemptCount = 1, NextLookupAtUtc = _time.Now.AddHours(1) },
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "3", LookupAttemptCount = 1, NextLookupAtUtc = _time.Now },
            new StreamTmdbMapping { ContentType = ContentType.Series, StreamId = "4", LookupAttemptCount = 1, NextLookupAtUtc = _time.Now.AddHours(1) }
        ]);
        await SaveAsync(source);

        var snapshot = await _service.GetSnapshotAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(["1"], snapshot.StreamTmdbMappings.Keys);
        Assert.Equal(["2"], snapshot.DeferredTmdbLookups);
    }

    [Fact]
    public async Task GetItemSnapshotAsync_LoadsOnlyTheMappingOfTheRequestedStream()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.ItemRules.Add(new ItemRule { ContentType = ContentType.Vod, Sequence = 10, Pattern = "rule" });
        source.StreamTmdbMappings.AddRange(
        [
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 },
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "2", TmdbId = 604 },
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "3", LookupAttemptCount = 1, NextLookupAtUtc = _time.Now.AddHours(1) },
            new StreamTmdbMapping { ContentType = ContentType.Series, StreamId = "2", TmdbId = 1399 }
        ]);
        await SaveAsync(source);

        var snapshot = await _service.GetItemSnapshotAsync(CreateContext(), "2", TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(source.Id, snapshot.Id);
        Assert.Equal(["rule"], snapshot.ItemRules.Select(rule => rule.Pattern));
        Assert.Equal(["2"], snapshot.StreamTmdbMappings.Keys);
        Assert.Equal(604, snapshot.StreamTmdbMappings["2"]);
        Assert.Empty(snapshot.DeferredTmdbLookups);
    }

    [Fact]
    public async Task GetItemSnapshotAsync_ExposesTheDeferredLookupOfTheRequestedStream()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.StreamTmdbMappings.AddRange(
        [
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 },
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "3", LookupAttemptCount = 1, NextLookupAtUtc = _time.Now.AddHours(1) }
        ]);
        await SaveAsync(source);

        var snapshot = await _service.GetItemSnapshotAsync(CreateContext(), "3", TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot.StreamTmdbMappings);
        Assert.Equal(["3"], snapshot.DeferredTmdbLookups);
    }

    [Fact]
    public async Task GetItemSnapshotAsync_WhenSourceIsUnknown_ReturnsNull()
    {
        var snapshot = await _service.GetItemSnapshotAsync(CreateContext(), "1", TestContext.Current.CancellationToken);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_LoadsTheEnabledTmdbRulesOfTheContentTypeInSequenceOrder()
    {
        await SaveAsync(new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 });
        _dbContext.TmdbRules.AddRange(
            new TmdbRule { ContentType = ContentType.Vod, Sequence = 20, Field = TmdbRuleField.Genre, Pattern = "second" },
            new TmdbRule { ContentType = ContentType.Vod, Sequence = 10, Field = TmdbRuleField.Title, Pattern = "first" },
            new TmdbRule { ContentType = ContentType.Vod, Sequence = 30, Pattern = "disabled", IsEnabled = false },
            new TmdbRule { ContentType = ContentType.Series, Sequence = 10, Pattern = "series" });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        var snapshot = await _service.GetSnapshotAsync(CreateContext(), TestContext.Current.CancellationToken);

        Assert.NotNull(snapshot);
        Assert.Equal(["first", "second"], snapshot.TmdbRules.Select(rule => rule.Pattern));
    }

    private async Task SaveAsync(XtreamSource source)
    {
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private static XtreamContext CreateContext()
        => new("http", "provider.example.com", 8080, "player_api.php", new DefaultHttpContext(), RequestAction.GetItems, ContentType.Vod);

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
