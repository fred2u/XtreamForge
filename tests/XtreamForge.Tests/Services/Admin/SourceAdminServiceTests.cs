using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class SourceAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SourceAdminService _service;

    public SourceAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new SourceAdminService(_dbContext);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsSourcesOrderedByHost()
    {
        _dbContext.XtreamSources.AddRange(
            new XtreamSource { Protocol = "http", Host = "c.example.com", Port = 80 },
            new XtreamSource { Protocol = "http", Host = "a.example.com", Port = 80 },
            new XtreamSource { Protocol = "http", Host = "b.example.com", Port = 80 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetAllAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a.example.com", "b.example.com", "c.example.com"], result.Select(s => s.Host).ToList());
    }

    [Fact]
    public async Task GetSummariesAsync_CountsCategoriesRulesAndMappingsPerSourceAndContentType()
    {
        var source = new XtreamSource { Protocol = "http", Host = "a.example.com", Port = 80 };
        var other = new XtreamSource { Protocol = "http", Host = "b.example.com", Port = 80 };
        var custom = new CustomCategory { Name = "Custom", ContentType = ContentType.Vod };
        _dbContext.XtreamSources.AddRange(source, other);
        _dbContext.CustomCategories.Add(custom);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.XtreamCategories.AddRange(
            new XtreamCategory { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Name = "Kept", XtreamId = "1", CustomCategoryId = custom.Id },
            new XtreamCategory { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Name = "Manual", XtreamId = "2", IsExcluded = true },
            new XtreamCategory { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Name = "Disabled", XtreamId = "3", IsEnabled = false },
            new XtreamCategory { XtreamSourceId = source.Id, ContentType = ContentType.Vod, Name = "XXX adult", XtreamId = "4" },
            new XtreamCategory { XtreamSourceId = source.Id, ContentType = ContentType.Series, Name = "Show", XtreamId = "5" },
            new XtreamCategory { XtreamSourceId = other.Id, ContentType = ContentType.Vod, Name = "Other", XtreamId = "1" });
        _dbContext.CategoryRules.AddRange(
            new CategoryRule
            {
                XtreamSourceId = source.Id,
                ContentType = ContentType.Vod,
                Sequence = 10,
                Action = RuleAction.Exclude,
                Operator = RuleOperator.Contains,
                Pattern = "xxx",
                IsEnabled = true
            },
            new CategoryRule
            {
                XtreamSourceId = source.Id,
                ContentType = ContentType.Vod,
                Sequence = 20,
                Action = RuleAction.Exclude,
                Operator = RuleOperator.Contains,
                Pattern = "Kept",
                IsEnabled = false
            });
        _dbContext.StreamTmdbMappings.AddRange(
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Vod, StreamId = "1", TmdbId = 10 },
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Series, StreamId = "2", TmdbId = 20 },
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Series, StreamId = "3", TmdbId = 30 },
            new StreamTmdbMapping { XtreamSourceId = source.Id, ContentType = ContentType.Vod, StreamId = "4", LookupAttemptCount = 1, NextLookupAtUtc = DateTimeOffset.UtcNow.AddDays(1) });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetSummariesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        var summary = result[0];
        Assert.Equal(source.Id, summary.Source.Id);
        Assert.Equal(new SourceContentSummary(4, 1, 1, 1, 1, 1, 2, 0, 1), summary.Vod);
        Assert.Equal(new SourceContentSummary(1, 1, 0, 0, 0, 0, 0, 0, 2), summary.Series);

        Assert.Equal(new SourceContentSummary(1, 1, 0, 0, 0, 0, 0, 0, 0), result[1].Vod);
        Assert.Equal(new SourceContentSummary(0, 0, 0, 0, 0, 0, 0, 0, 0), result[1].Series);
    }

    [Fact]
    public async Task FindAsync_WhenExists_ReturnsSource()
    {
        var source = new XtreamSource { Protocol = "https", Host = "s.example.com", Port = 443 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.FindAsync(source.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("s.example.com", result.Host);
        Assert.Equal(443, result.Port);
    }

    [Fact]
    public async Task FindAsync_WhenNotFound_ReturnsNull()
    {
        var result = await _service.FindAsync(999, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsFalse()
    {
        var deleted = await _service.DeleteAsync(999, TestContext.Current.CancellationToken);

        Assert.False(deleted);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
