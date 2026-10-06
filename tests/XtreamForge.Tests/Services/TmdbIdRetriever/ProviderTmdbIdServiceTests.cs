using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.TmdbIdRetriever;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.TmdbIdRetriever;

public class ProviderTmdbIdServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SteppingTimeProvider _time = new();

    public ProviderTmdbIdServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task SaveAsync_WhenStreamsAreNotMapped_PersistsTheirTmdbIds()
    {
        var sourceId = await CreateSourceAsync();

        var saved = await CreateService().SaveAsync(
            new ProviderTmdbIdRequest(sourceId, ContentType.Series, new Dictionary<string, long> { ["1"] = 101, ["2"] = 102 }),
            TestContext.Current.CancellationToken);

        Assert.True(saved);
        var mappings = await _dbContext.StreamTmdbMappings.AsNoTracking().OrderBy(mapping => mapping.StreamId).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [(sourceId, ContentType.Series, "1", (long?)101), (sourceId, ContentType.Series, "2", 102)],
            mappings.Select(mapping => (mapping.XtreamSourceId, mapping.ContentType, mapping.StreamId, mapping.TmdbId)));
        Assert.All(mappings, mapping => Assert.Equal(_time.Now, mapping.CreatedAtUtc));
    }

    [Fact]
    public async Task SaveAsync_WhenTheLookupFoundNothing_CompletesTheMappingWithoutFurtherLookup()
    {
        var sourceId = await CreateSourceAsync();
        await AddMappingAsync(sourceId, "1", tmdbId: null, nextLookupAtUtc: _time.Now.AddDays(1));

        var saved = await CreateService().SaveAsync(
            new ProviderTmdbIdRequest(sourceId, ContentType.Vod, new Dictionary<string, long> { ["1"] = 101 }),
            TestContext.Current.CancellationToken);

        Assert.True(saved);
        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(101, mapping.TmdbId);
        Assert.Null(mapping.NextLookupAtUtc);
        Assert.Equal(_time.Now, mapping.UpdatedAtUtc);
    }

    [Fact]
    public async Task SaveAsync_WhenTheStreamIsMapped_KeepsThePersistedTmdbId()
    {
        var sourceId = await CreateSourceAsync();
        await AddMappingAsync(sourceId, "1", tmdbId: 999, nextLookupAtUtc: null);

        var saved = await CreateService().SaveAsync(
            new ProviderTmdbIdRequest(sourceId, ContentType.Vod, new Dictionary<string, long> { ["1"] = 101 }),
            TestContext.Current.CancellationToken);

        Assert.False(saved);
        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(999, mapping.TmdbId);
    }

    [Fact]
    public async Task SaveAsync_KeepsTheMappingsOfAnotherContentTypeOrSourceApart()
    {
        var sourceId = await CreateSourceAsync();
        var otherSourceId = await CreateSourceAsync("other.example.com");
        await AddMappingAsync(sourceId, "1", tmdbId: 999, nextLookupAtUtc: null, ContentType.Series);
        await AddMappingAsync(otherSourceId, "1", tmdbId: 998, nextLookupAtUtc: null);

        await CreateService().SaveAsync(
            new ProviderTmdbIdRequest(sourceId, ContentType.Vod, new Dictionary<string, long> { ["1"] = 101 }),
            TestContext.Current.CancellationToken);

        var mapping = await _dbContext.StreamTmdbMappings.AsNoTracking()
            .SingleAsync(mapping => mapping.XtreamSourceId == sourceId && mapping.ContentType == ContentType.Vod, TestContext.Current.CancellationToken);
        Assert.Equal(101, mapping.TmdbId);
        Assert.Equal(3, await _dbContext.StreamTmdbMappings.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task<int> CreateSourceAsync(string host = "provider.example.com")
    {
        var source = new XtreamSource { Protocol = "http", Host = host, Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        return source.Id;
    }

    private async Task AddMappingAsync(int xtreamSourceId, string streamId, long? tmdbId, DateTimeOffset? nextLookupAtUtc, ContentType contentType = ContentType.Vod)
    {
        _dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping
        {
            XtreamSourceId = xtreamSourceId,
            ContentType = contentType,
            StreamId = streamId,
            TmdbId = tmdbId,
            LookupAttemptCount = tmdbId is null ? 1 : 0,
            NextLookupAtUtc = nextLookupAtUtc
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    private ProviderTmdbIdService CreateService() => new(_dbContext, _time);

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
