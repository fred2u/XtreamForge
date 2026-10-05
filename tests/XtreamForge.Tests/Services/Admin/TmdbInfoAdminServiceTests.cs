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
            [(null, InclusionDecision.Include, null), ("Comet", InclusionDecision.Exclude, TmdbExclusionReason.Rule), ("Lattice", InclusionDecision.Include, null), ("Mossbeard", InclusionDecision.Exclude, (TmdbExclusionReason?)TmdbExclusionReason.ManuallyExcluded)],
            page.Items.Select(item => (item.Info.Title, item.Decision, item.ExclusionReason)));
        Assert.Equal((4, 4, 2, 1, 1), (page.MatchingCount, page.TotalCount, page.ExcludedCount, page.ManuallyExcludedCount, page.NotLoadedCount));
        Assert.Empty(_dbContext.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("latt", new[] { "Lattice" })]
    [InlineData("the lattice", new[] { "Lattice" })]
    [InlineData("603", new[] { "Lattice" })]
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

        Assert.Equal(["Mossbeard"], excluded.Items.Select(item => item.Info.Title));
        Assert.Equal(3, notExcludedManually.MatchingCount);
        Assert.Equal([999L], notLoaded.Items.Select(item => item.Info.TmdbId));
    }

    [Theory]
    [InlineData("Horror", new[] { "Comet" })]
    [InlineData(" sCIENCE fiction ", new[] { "Comet", "Lattice" })]
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

        var page = await _service.GetPageAsync(new TmdbInfoListQuery(ContentType.Vod, Search: "Mossbeard"), TestContext.Current.CancellationToken);

        Assert.Equal(["Horror", "Science Fiction"], page.Genres);
    }

    [Theory]
    [InlineData(1, 2, new[] { "Comet", "Lattice" })]
    [InlineData(-5, 0, new string?[] { null })]
    [InlineData(0, 1000, new[] { null, "Comet", "Lattice", "Mossbeard" })]
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
        var info = new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice", Overview = "Orion", Cast = ["Aldo Ferrant"], NextLoadAtUtc = DateTimeOffset.UtcNow };
        _dbContext.TmdbInfos.Add(info);
        var rule = new TmdbRule { ContentType = ContentType.Vod, Sequence = 10, Field = TmdbRuleField.Title, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Lattice" };
        _dbContext.TmdbRules.Add(rule);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var evaluation = await _service.GetAsync(info.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(evaluation);
        Assert.Equal(("Orion", "Aldo Ferrant"), (evaluation.Info.Overview, Assert.Single(evaluation.Info.Cast)));
        Assert.Equal((InclusionDecision.Exclude, rule.Id), (evaluation.Decision, evaluation.DecidingRule?.Id));
        Assert.Null(await _service.GetAsync(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetExcludedAsync_UpdatesTheManualExclusion()
    {
        var info = new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice", NextLoadAtUtc = DateTimeOffset.UtcNow };
        _dbContext.TmdbInfos.Add(info);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        Assert.True(await _service.SetExcludedAsync(info.Id, true, TestContext.Current.CancellationToken));
        Assert.False(await _service.SetExcludedAsync(999, true, TestContext.Current.CancellationToken));

        Assert.True((await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).IsExcluded);
    }

    // Comet (Horror), Lattice (original title "The Lattice"), Mossbeard (excluded manually), an entry not loaded yet, and a series
    private async Task SeedAsync()
    {
        var now = DateTimeOffset.UtcNow;
        _dbContext.TmdbInfos.AddRange(
            new TmdbInfo { TmdbId = 348, ContentType = ContentType.Vod, Title = "Comet", Genres = ["Horror", "Science Fiction"], LoadedAtUtc = now, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Lattice", OriginalTitle = "The Lattice", Genres = ["Science Fiction"], LoadedAtUtc = now, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 808, ContentType = ContentType.Vod, Title = "Mossbeard", IsExcluded = true, LoadedAtUtc = now, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 999, ContentType = ContentType.Vod, NextLoadAtUtc = now },
            new TmdbInfo { TmdbId = 1399, ContentType = ContentType.Series, Title = "Crowns of Ash", Genres = ["Drama"], LoadedAtUtc = now, NextLoadAtUtc = now });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
