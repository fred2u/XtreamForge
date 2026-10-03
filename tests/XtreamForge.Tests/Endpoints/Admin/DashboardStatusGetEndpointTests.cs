using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Endpoints.Admin.Dashboard;
using XtreamForge.ApiService.Endpoints.Admin.Dashboard.Dto;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class DashboardStatusGetEndpointTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly DashboardStatusGetEndpoint _endpoint;

    public DashboardStatusGetEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _endpoint = new DashboardStatusGetEndpoint(_dbContext, NullLogger<DashboardStatusGetEndpoint>.Instance);
    }

    [Fact]
    public async Task GetAsync_WhenDatabaseQueryFails_ReturnsUnavailableStatus()
    {
        await _dbContext.Database.ExecuteSqlRawAsync("DROP TABLE stream_tmdb_mappings", TestContext.Current.CancellationToken);

        var result = await _endpoint.GetAsync(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<DashboardStatusDto>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal("Unavailable", ok.Value.DatabaseStatus);
        Assert.Equal(0, ok.Value.KnownTmdbMappingCount);
    }

    [Fact]
    public async Task GetAsync_CountsKnownAndUnresolvedTmdbMappings()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        source.StreamTmdbMappings.AddRange(
        [
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 },
            new StreamTmdbMapping { ContentType = ContentType.Series, StreamId = "1", TmdbId = 1399 },
            new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "2", LookupAttemptCount = 1, NextLookupAtUtc = DateTimeOffset.UtcNow.AddDays(1) }
        ]);
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _endpoint.GetAsync(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<DashboardStatusDto>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(2, ok.Value.KnownTmdbMappingCount);
        Assert.Equal(1, ok.Value.UnresolvedTmdbMappingCount);
    }

    [Fact]
    public async Task GetAsync_CountsTmdbInfosAndRules()
    {
        var now = DateTimeOffset.UtcNow;
        _dbContext.TmdbInfos.AddRange(
            new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", LoadedAtUtc = now, NextLoadAtUtc = now.AddDays(60) },
            new TmdbInfo { TmdbId = 808, ContentType = ContentType.Vod, Title = "Shrek", IsExcluded = true, LoadedAtUtc = now, NextLoadAtUtc = now.AddDays(60) },
            new TmdbInfo { TmdbId = 1399, ContentType = ContentType.Series, NextLoadAtUtc = now });
        _dbContext.TmdbRules.Add(new TmdbRule { ContentType = ContentType.Vod, Sequence = 10, Pattern = "Horror" });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _endpoint.GetAsync(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<DashboardStatusDto>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(
            (3, 1, 1, 1),
            (ok.Value.TmdbInfoCount, ok.Value.NotLoadedTmdbInfoCount, ok.Value.ManuallyExcludedTmdbInfoCount, ok.Value.TmdbRuleCount));
    }

    [Fact]
    public async Task GetAsync_WhenNoTmdbDataExists_ReturnsZero()
    {
        var result = await _endpoint.GetAsync(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<DashboardStatusDto>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal(
            (0, 0, 0, 0, 0, 0),
            (ok.Value.KnownTmdbMappingCount, ok.Value.UnresolvedTmdbMappingCount, ok.Value.TmdbInfoCount,
                ok.Value.NotLoadedTmdbInfoCount, ok.Value.ManuallyExcludedTmdbInfoCount, ok.Value.TmdbRuleCount));
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
