using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class TmdbInfoAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbInfoAdminService _service;

    public TmdbInfoAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _service = new TmdbInfoAdminService(_dbContext);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsTheEntriesOfTheContentTypeByTitleWithTheirDecision()
    {
        await SeedAsync();
        _dbContext.TmdbRules.Add(new TmdbRule { ContentType = ContentType.Vod, Sequence = 10, Field = TmdbRuleField.Genre, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Horror" });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        var page = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod), TestContext.Current.CancellationToken);

        Assert.Equal(
            // an entry without title is sorted first
            [(null, InclusionDecision.Include, null), ("Alien", InclusionDecision.Exclude, TmdbExclusionReason.Rule), ("Matrix", InclusionDecision.Include, null), ("Shrek", InclusionDecision.Exclude, (TmdbExclusionReason?)TmdbExclusionReason.ManuallyExcluded)],
            page.Items.Select(item => (item.Info.Title, item.Decision, item.ExclusionReason)));
        Assert.Equal((4, 4, 2, 1, 1), (page.MatchingCount, page.TotalCount, page.ExcludedCount, page.ManuallyExcludedCount, page.NotLoadedCount));
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("matr", new[] { "Matrix" })]
    [InlineData("the matrix", new[] { "Matrix" })]
    [InlineData("603", new[] { "Matrix" })]
    [InlineData("60", new string[0])]
    public async Task GetPageAsync_SearchMatchesTitleOriginalTitleOrExactTmdbId(string search, string[] expectedTitles)
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, Search: search), TestContext.Current.CancellationToken);

        Assert.Equal(expectedTitles, page.Items.Select(item => item.Info.Title));
        Assert.Equal(expectedTitles.Length, page.MatchingCount);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task GetPageAsync_FiltersByDecisionManualExclusionAndLoadState()
    {
        await SeedAsync();

        var excluded = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, Decision: InclusionDecision.Exclude), TestContext.Current.CancellationToken);
        var notExcludedManually = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, IsExcluded: false), TestContext.Current.CancellationToken);
        var notLoaded = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, IsLoaded: false), TestContext.Current.CancellationToken);

        Assert.Equal(["Shrek"], excluded.Items.Select(item => item.Info.Title));
        Assert.Equal(3, notExcludedManually.MatchingCount);
        Assert.Equal([999L], notLoaded.Items.Select(item => item.Info.TmdbId));
    }

    [Theory]
    [InlineData("Horror", new[] { "Alien" })]
    [InlineData(" sCIENCE fiction ", new[] { "Alien", "Matrix" })]
    [InlineData("Science", new string[0])]
    public async Task GetPageAsync_GenreMatchesOneOfTheGenreNamesIgnoringCase(string genre, string[] expectedTitles)
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, Genre: genre), TestContext.Current.CancellationToken);

        Assert.Equal(expectedTitles, page.Items.Select(item => item.Info.Title));
        Assert.Equal(expectedTitles.Length, page.MatchingCount);
    }

    [Fact]
    public async Task GetPageAsync_ReturnsTheSortedGenresOfTheContentTypeWhateverTheFilters()
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, Search: "Shrek"), TestContext.Current.CancellationToken);

        Assert.Equal(["Horror", "Science Fiction"], page.Genres);
    }

    [Theory]
    [InlineData(1, 2, new[] { "Alien", "Matrix" })]
    [InlineData(-5, 0, new string?[] { null })]
    [InlineData(0, 1000, new[] { null, "Alien", "Matrix", "Shrek" })]
    public async Task GetPageAsync_PagesTheMatchingEntries(int skip, int take, string?[] expectedTitles)
    {
        await SeedAsync();

        var page = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, Skip: skip, Take: take), TestContext.Current.CancellationToken);

        Assert.Equal(expectedTitles, page.Items.Select(item => item.Info.Title));
        Assert.Equal(4, page.MatchingCount);
    }

    [Fact]
    public async Task GetAsync_ReturnsEveryValueWithTheDecision()
    {
        var info = new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", Overview = "Neo", Cast = ["Keanu Reeves"], NextLoadAtUtc = DateTimeOffset.UtcNow };
        _dbContext.TmdbInfos.Add(info);
        var rule = new TmdbRule { ContentType = ContentType.Vod, Sequence = 10, Field = TmdbRuleField.Title, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Matrix" };
        _dbContext.TmdbRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evaluation = await _service.GetAsync(info.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(evaluation);
        Assert.Equal(("Neo", "Keanu Reeves"), (evaluation.Info.Overview, Assert.Single(evaluation.Info.Cast)));
        Assert.Equal((InclusionDecision.Exclude, rule.Id), (evaluation.Decision, evaluation.DecidingRule?.Id));
        Assert.Null(await _service.GetAsync(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetExcludedAsync_UpdatesTheManualExclusion()
    {
        var info = new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", NextLoadAtUtc = DateTimeOffset.UtcNow };
        _dbContext.TmdbInfos.Add(info);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        Assert.True(await _service.SetExcludedAsync(info.Id, true, TestContext.Current.CancellationToken));
        Assert.False(await _service.SetExcludedAsync(999, true, TestContext.Current.CancellationToken));

        Assert.True((await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).IsExcluded);
    }

    // Alien (Horror), Matrix (original title "The Matrix"), Shrek (excluded manually), an entry not loaded yet, and a series
    private async Task SeedAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _dbContext.TmdbInfos.AddRange(
            new TmdbInfo { TmdbId = 348, ContentType = ContentType.Vod, Title = "Alien", Genres = ["Horror", "Science Fiction"], LoadedAtUtc = now, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", OriginalTitle = "The Matrix", Genres = ["Science Fiction"], LoadedAtUtc = now, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 808, ContentType = ContentType.Vod, Title = "Shrek", IsExcluded = true, LoadedAtUtc = now, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 999, ContentType = ContentType.Vod, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 1399, ContentType = ContentType.Series, Title = "Game of Thrones", Genres = ["Drama"], LoadedAtUtc = now, NextLoadAtUtc = now });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
