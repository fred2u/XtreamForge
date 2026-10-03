using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Services.Tmdb.Scoring;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Tmdb;

public class TmdbScoringRuleTests
{
    [Theory]
    [InlineData("The Matrix", "Matrix", "The Matrix", 35)]
    [InlineData("Matrix The Matrix", "Matrix", "The Matrix", 35)]
    [InlineData("Fast and Furious", "Fast & Furious", "Fast & Furious", 35)]
    [InlineData("Les Évadés", "Les Evades", "The Shawshank Redemption", 35)]
    [InlineData("Les Evades (The Shawshank Redemption)", "Les Évadés", "The Shawshank Redemption", 35)]
    [InlineData("Les Evades (Rita Hayworth)", "Les Évadés", "The Shawshank Redemption", 30)]
    [InlineData("Les Evades (Other)", "Something", "Different", 0)]
    [InlineData("The Lord of the Rings Extended", "The Lord of the Rings", "The Lord of the Rings", 20)]
    [InlineData("The Matrixx", "The Matrix", "The Matrix", 30)]
    [InlineData("Alien", "Aliens vs Predator", "Aliens vs Predator", 0)]
    public async Task TitleScoringRule_ScoresTitleProximity(string sourceTitle, string candidateTitle, string candidateOriginalTitle, int expectedScore)
    {
        var context = CreateContext(new TmdbSourceItem { Title = sourceTitle }, new TmdbCandidate { Title = candidateTitle, OriginalTitle = candidateOriginalTitle });

        Assert.Equal(expectedScore, await new TitleScoringRule().ScoreAsync(context, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("f89U3ADr1oiB1s9GkdPOEpXUk5H", "/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg", 40)]
    [InlineData("F89U3ADR1OIB1S9GKDPOEPXUK5H", "/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg", 40)]
    [InlineData("f89U3ADr1oiB1s9GkdPOEpXUk5H", "/other.jpg", 0)]
    [InlineData("cover", "/cover.jpg", 0)]
    [InlineData("", null, 0)]
    public async Task PosterScoringRule_ScoresIdenticalPoster(string sourcePosterId, string? candidatePosterPath, int expectedScore)
    {
        var context = CreateContext(new TmdbSourceItem { PosterId = sourcePosterId }, new TmdbCandidate { PosterPath = candidatePosterPath });

        Assert.Equal(expectedScore, await new PosterScoringRule().ScoreAsync(context, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("1999-03-31", "1999-03-31", 25)]
    [InlineData("1999-03-31", "1999-04-05", 20)]
    [InlineData("1999-03-31", "1999-04-15", 10)]
    [InlineData("1999-01-01", "1999-12-31", 10)]
    [InlineData("1999-12-31", "2000-01-10", 15)]
    [InlineData("1999-03-31", "2003-03-31", 0)]
    [InlineData(null, "1999-03-31", 0)]
    public async Task ReleaseDateScoringRule_ScoresDateProximity(string? sourceDate, string? candidateDate, int expectedScore)
    {
        var context = CreateContext(
            new TmdbSourceItem { ReleaseDate = TmdbText.ParseDate(sourceDate) },
            new TmdbCandidate { ReleaseDate = TmdbText.ParseDate(candidateDate) });

        Assert.Equal(expectedScore, await new ReleaseDateScoringRule().ScoreAsync(context, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(new[] { "keanu reeves", "carrie anne moss" }, new[] { "keanu reeves", "carrie anne moss", "laurence fishburne" }, 20)]
    [InlineData(new[] { "keanu reeves", "unknown actor" }, new[] { "keanu reeves" }, 10)]
    [InlineData(new[] { "unknown actor" }, new[] { "keanu reeves" }, 0)]
    [InlineData(new string[0], new[] { "keanu reeves" }, 5)]
    [InlineData(new[] { "keanu reeves" }, new string[0], 5)]
    public async Task CastScoringRules_ScoreOverlapWithNeutralWhenUnknown(string[] sourceValues, string[] candidateValues, int expectedScore)
    {
        var context = CreateContext(
            new TmdbSourceItem { Cast = sourceValues.ToHashSet(), Genres = sourceValues.ToHashSet() },
            new TmdbCandidate { Cast = candidateValues.ToHashSet(), Genres = candidateValues.ToHashSet() });

        Assert.Equal(expectedScore, await new CastScoringRule().ScoreAsync(context, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(new[] { "keanu reeves", "carrie anne moss" }, new[] { "keanu reeves", "carrie anne moss", "laurence fishburne" }, 10)]
    [InlineData(new[] { "keanu reeves", "unknown actor" }, new[] { "keanu reeves" }, 5)]
    [InlineData(new[] { "unknown actor" }, new[] { "keanu reeves" }, 0)]
    [InlineData(new string[0], new[] { "keanu reeves" }, 5)]
    [InlineData(new[] { "keanu reeves" }, new string[0], 5)]
    public async Task GenreScoringRules_ScoreOverlapWithNeutralWhenUnknown(string[] sourceValues, string[] candidateValues, int expectedScore)
    {
        var context = CreateContext(
            new TmdbSourceItem { Cast = sourceValues.ToHashSet(), Genres = sourceValues.ToHashSet() },
            new TmdbCandidate { Cast = candidateValues.ToHashSet(), Genres = candidateValues.ToHashSet() });

        Assert.Equal(expectedScore, await new GenreScoringRule().ScoreAsync(context, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 0)]
    public async Task SingleCandidateScoringRule_ScoresUniqueResult(int candidateCount, int expectedScore)
    {
        var context = new TmdbScoringContext(new TmdbSourceItem(), new TmdbCandidate(), candidateCount);

        Assert.Equal(expectedScore, await new SingleCandidateScoringRule().ScoreAsync(context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SeasonScoringRule_AppliesToSeriesOnlyInAdvancedStage()
    {
        var rule = new SeasonScoringRule(new StubTmdbHttpClientFactory(_ => null).CreateTmdbClient());

        Assert.Equal(TmdbScoringStage.Advanced, rule.Stage);
        Assert.True(rule.AppliesTo(ContentType.Series));
        Assert.False(rule.AppliesTo(ContentType.Vod));
    }

    [Theory]
    // all air dates match (15) and the episode count matches (5)
    [InlineData("2011-04-17", "2011-04-24", 2, 20)]
    // one day of tolerance
    [InlineData("2011-04-18", "2011-04-25", 2, 20)]
    // half of the air dates match (8) and the episode count differs (0)
    [InlineData("2011-04-17", "2011-05-24", 3, 8)]
    public async Task SeasonScoringRule_ComparesAirDatesAndEpisodeCounts(string firstAirDate, string secondAirDate, int sourceEpisodeCount, int expectedScore)
    {
        var rule = CreateSeasonRule();
        var source = new TmdbSourceItem
        {
            Seasons = [new TmdbSourceSeason(1, sourceEpisodeCount)],
            Episodes =
            [
                new TmdbSourceEpisode(1, 1, TmdbText.ParseDate(firstAirDate)),
                new TmdbSourceEpisode(1, 2, TmdbText.ParseDate(secondAirDate))
            ]
        };

        var score = await rule.ScoreAsync(CreateContext(source, new TmdbCandidate { Id = 1399 }), TestContext.Current.CancellationToken);

        Assert.Equal(expectedScore, score);
    }

    [Fact]
    public async Task SeasonScoringRule_WhenAirDatesAreUnknown_ScoresEpisodeCounts()
    {
        var source = new TmdbSourceItem { Seasons = [new TmdbSourceSeason(1, 2)] };

        var score = await CreateSeasonRule().ScoreAsync(CreateContext(source, new TmdbCandidate { Id = 1399 }), TestContext.Current.CancellationToken);

        Assert.Equal(15, score);
    }

    [Fact]
    public async Task SeasonScoringRule_WhenSeasonIsUnknownByTmdb_IgnoresIt()
    {
        var source = new TmdbSourceItem { Seasons = [new TmdbSourceSeason(9, 2)] };

        var score = await CreateSeasonRule().ScoreAsync(CreateContext(source, new TmdbCandidate { Id = 1399 }), TestContext.Current.CancellationToken);

        Assert.Equal(5, score);
    }

    [Fact]
    public async Task SeasonScoringRule_WhenNoSeasonIsKnown_ScoresZero()
    {
        var factory = new StubTmdbHttpClientFactory(_ => null);
        var rule = new SeasonScoringRule(factory.CreateTmdbClient());

        var score = await rule.ScoreAsync(CreateContext(new TmdbSourceItem(), new TmdbCandidate { Id = 1399 }), TestContext.Current.CancellationToken);

        Assert.Equal(0, score);
        Assert.Empty(factory.RequestedUris);
    }

    private static SeasonScoringRule CreateSeasonRule()
    {
        var factory = new StubTmdbHttpClientFactory(new Dictionary<string, string>
        {
            ["tv/1399/season/1"] = """{ "episodes": [{ "episode_number": 1, "air_date": "2011-04-17" }, { "episode_number": 2, "air_date": "2011-04-24" }] }"""
        });

        return new SeasonScoringRule(factory.CreateTmdbClient());
    }

    private static TmdbScoringContext CreateContext(TmdbSourceItem source, TmdbCandidate candidate) => new(source, candidate, 2);
}
