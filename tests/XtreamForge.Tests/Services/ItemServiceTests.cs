using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services;

public class ItemServiceTests
{
    private const int SourceId = 5;
    private const long TmdbId = 603;

    // upstream category 10 is exposed as XtreamForge category 100
    private static readonly Dictionary<string, int> XtreamCategoryIdMapping = new(StringComparer.OrdinalIgnoreCase) { ["10"] = 100 };

    private readonly TmdbIdRetrieverQueue _queue = new();
    private readonly TmdbInfoQueue _tmdbInfoQueue = new();
    private readonly SteppingTimeProvider _time = new();
    private readonly ItemService _service;

    public ItemServiceTests()
    {
        _service = CreateService(tmdbApiKey: "token");
    }

    [Fact]
    public void TransformStreamItem_WhenItemIsNotAnObject_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);

        var result = Transform("[1, 2]", context, []);

        Assert.Null(result);
    }

    [Fact]
    public void TransformStreamItem_WhenIdentifierIsMissing_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);

        var result = Transform("""{ "name": "Movie", "category_id": "10", "tmdb_id": "555" }""", context, []);

        Assert.Null(result);
    }

    [Fact]
    public void TransformStreamItem_ForSeries_UsesSeriesIdAsIdentifier()
    {
        var context = CreateContext(ContentType.Series);

        var withStreamId = Transform("""{ "stream_id": 1, "name": "Show", "category_id": "10", "tmdb_id": "555" }""", context, []);
        var withSeriesId = Transform("""{ "series_id": 1, "name": "Show", "category_id": "10", "tmdb_id": "555" }""", context, []);

        Assert.Null(withStreamId);
        Assert.NotNull(withSeriesId);
    }

    [Fact]
    public void TransformStreamItem_WhenIdentifierAlreadySeen_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);
        var seenIds = new HashSet<string>();
        const string json = """{ "stream_id": 42, "name": "Movie", "category_id": "10", "tmdb_id": "555" }""";

        var first = Transform(json, context, seenIds);
        var second = Transform(json, context, seenIds);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TransformStreamItem_WhenCategoryIsNotMapped_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);

        var result = Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "999", "tmdb_id": "555" }""", context, []);

        Assert.Null(result);
    }

    [Fact]
    public void TransformStreamItem_RewritesCategoryReferencesToInternalId()
    {
        var context = CreateContext(ContentType.Vod);

        var result = Transform(
            """{ "stream_id": 1, "name": "Movie", "category_id": "10", "tmdb_id": "555", "category_ids": [10, 11] }""",
            context,
            []);

        Assert.NotNull(result);
        Assert.Equal("100", result["category_id"]?.GetValue<string>());
        var categoryIds = Assert.IsType<JsonArray>(result["category_ids"]);
        Assert.Equal("100", Assert.Single(categoryIds)?.GetValue<string>());
        Assert.Equal("Movie", result["name"]?.GetValue<string>());
    }

    [Fact]
    public void TransformStreamItem_WhenItemRuleExcludes_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);
        var source = CreateSource(itemRules: new ItemRule
        {
            Sequence = 1,
            Action = RuleAction.Exclude,
            Operator = RuleOperator.StartsWith,
            Pattern = "[XXX]"
        });

        var excluded = Transform("""{ "stream_id": 1, "name": "[XXX] Movie", "category_id": "10", "tmdb_id": "555" }""", context, [], source);
        var included = Transform("""{ "stream_id": 2, "name": "Movie", "category_id": "10", "tmdb_id": "555" }""", context, [], source);

        Assert.Null(excluded);
        Assert.NotNull(included);
    }

    [Fact]
    public void TransformStreamItem_WhenItemHasTmdbId_KeepsProviderTmdbId()
    {
        var context = CreateContext(ContentType.Vod);
        var source = CreateSource(new Dictionary<string, long> { ["1"] = 999 });

        var result = Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "10", "tmdb_id": "555" }""", context, [], source);

        Assert.NotNull(result);
        Assert.Equal("555", result["tmdb_id"]?.ToString());
    }

    [Theory]
    [InlineData("""{ "stream_id": 1, "name": "Movie", "category_id": "10" }""")]
    [InlineData("""{ "stream_id": 1, "name": "Movie", "category_id": "10", "tmdb_id": null }""")]
    [InlineData("""{ "stream_id": 1, "name": "Movie", "category_id": "10", "tmdb_id": " " }""")]
    public void TransformStreamItem_WhenTmdbIdMissingAndMapped_UsesMappedTmdbId(string json)
    {
        var context = CreateContext(ContentType.Vod);
        var source = CreateSource(new Dictionary<string, long> { ["1"] = 777 });

        var result = Transform(json, context, [], source);

        Assert.NotNull(result);
        Assert.Equal("777", result["tmdb_id"]?.GetValue<string>());
    }

    [Fact]
    public void TransformStreamItem_ForSeries_UsesSeriesIdToLookUpTmdbMapping()
    {
        var context = CreateContext(ContentType.Series);
        var source = CreateSource(new Dictionary<string, long> { ["7"] = 888 });

        var result = Transform("""{ "series_id": 7, "name": "Show", "category_id": "10" }""", context, [], source);

        Assert.NotNull(result);
        Assert.Equal("888", result["tmdb_id"]?.GetValue<string>());
    }

    [Fact]
    public void TransformStreamItem_WhenTmdbIdMissingAndNoMappings_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);

        var result = Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "10" }""", context, []);

        Assert.Null(result);
    }

    [Fact]
    public void TransformStreamItem_WhenTmdbIdMissingAndStreamNotMapped_ReturnsNull()
    {
        var context = CreateContext(ContentType.Vod);
        var source = CreateSource(new Dictionary<string, long> { ["2"] = 777 });

        var result = Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "10", "tmdb_id": "" }""", context, [], source);

        Assert.Null(result);
    }

    [Fact]
    public async Task TransformStreamItem_WhenTmdbIdIsUnknown_EnqueuesLookupWithStreamIcon()
    {
        var context = CreateContext(ContentType.Vod, "?username=user&password=secret");

        var result = Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "10", "stream_icon": "http://img/poster.jpg" }""", context, []);

        Assert.Null(result);
        var request = await ReadSingleQueuedRequestAsync();
        Assert.Equal(new TmdbIdRetrieverRequest(SourceId, "http", "provider.example.com", 8080, "user", "secret", "1", ContentType.Vod, "http://img/poster.jpg"), request);
    }

    [Theory]
    [InlineData("?password=secret")]
    [InlineData("?username=user")]
    public void TransformStreamItem_WhenCredentialsAreMissing_DoesNotEnqueueLookup(string queryString)
    {
        var context = CreateContext(ContentType.Vod, queryString);

        Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "10" }""", context, []);

        // the same stream can still be enqueued, so nothing is pending
        Assert.True(_queue.TryEnqueue(new TmdbIdRetrieverRequest(SourceId, "http", "provider.example.com", 8080, "user", "secret", "1", ContentType.Vod)));
    }

    [Fact]
    public void TransformStreamItem_WhenTmdbLookupIsDeferred_DoesNotEnqueueLookup()
    {
        var context = CreateContext(ContentType.Vod, "?username=user&password=secret");
        var source = CreateSource(deferredTmdbLookups: ["1"]);

        var result = Transform("""{ "stream_id": 1, "name": "Movie", "category_id": "10" }""", context, [], source);

        Assert.Null(result);
        Assert.Equal(0, _queue.Count);
    }

    [Fact]
    public void TransformInfo_ForVod_RewritesCategoriesAndInjectsMappedTmdbId()
    {
        var source = CreateSource(new Dictionary<string, long> { ["1"] = 777 });

        var result = TransformInfo(
            """{ "info": { "name": "Movie", "category_id": "10" }, "movie_data": { "stream_id": 1, "name": "Movie", "category_id": "10", "category_ids": [10, 11] } }""",
            ContentType.Vod,
            source);

        Assert.NotNull(result);
        var info = Assert.IsType<JsonObject>(result["info"]);
        var movieData = Assert.IsType<JsonObject>(result["movie_data"]);
        Assert.Equal("777", info["tmdb_id"]?.GetValue<string>());
        Assert.Equal("100", info["category_id"]?.GetValue<string>());
        Assert.Equal("100", movieData["category_id"]?.GetValue<string>());
        Assert.Equal("100", Assert.Single(Assert.IsType<JsonArray>(movieData["category_ids"]))?.GetValue<string>());
    }

    [Theory]
    [InlineData("""{ "info": { "tmdb_id": "555" }, "movie_data": { "name": "Movie", "category_id": "10" } }""")]
    [InlineData("""{ "info": [], "movie_data": { "name": "Movie", "category_id": "10", "tmdb_id": 555 } }""")]
    public void TransformInfo_ForVod_WhenProviderHasTmdbIdAndStreamIsNotMapped_KeepsIt(string json)
    {
        var source = CreateSource(new Dictionary<string, long> { ["2"] = 777 });

        var result = TransformInfo(json, ContentType.Vod, source);

        Assert.NotNull(result);
        Assert.Equal(555, ItemService.ReadInfoTmdbId(result, ContentType.Vod));
        Assert.DoesNotContain("777", result.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TransformInfo_ForVod_WhenStreamIsMapped_ReplacesProviderTmdbIds()
    {
        var source = CreateSource(new Dictionary<string, long> { ["1"] = 777 });

        var result = TransformInfo(
            """{ "info": { "tmdb_id": "555" }, "movie_data": { "name": "Movie", "category_id": "10", "tmdb_id": 555 } }""",
            ContentType.Vod,
            source);

        Assert.NotNull(result);
        Assert.Equal("777", result["info"]?["tmdb_id"]?.GetValue<string>());
        Assert.Equal("777", result["movie_data"]?["tmdb_id"]?.GetValue<string>());
    }

    [Fact]
    public void TransformInfo_ForVod_WhenInfoIsNotAnObject_CreatesItForMappedTmdbId()
    {
        var source = CreateSource(new Dictionary<string, long> { ["1"] = 777 });

        var result = TransformInfo("""{ "info": [], "movie_data": { "name": "Movie", "category_id": "10" } }""", ContentType.Vod, source);

        Assert.NotNull(result);
        Assert.Equal("777", result["info"]?["tmdb_id"]?.GetValue<string>());
    }

    [Fact]
    public void TransformInfo_ForSeries_UsesInfoSection()
    {
        var source = CreateSource(new Dictionary<string, long> { ["1"] = 888 });

        var result = TransformInfo(
            """{ "seasons": [], "info": { "name": "Show", "category_id": "10" }, "episodes": {}, "movie_data": { "category_id": "999" } }""",
            ContentType.Series,
            source);

        Assert.NotNull(result);
        Assert.Equal("100", result["info"]?["category_id"]?.GetValue<string>());
        Assert.Equal("888", result["info"]?["tmdb_id"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{ "info": { "name": "Movie", "category_id": "10", "tmdb_id": "555" } }""")]
    [InlineData("""{ "info": { "tmdb_id": "555" }, "movie_data": { "name": "Movie", "category_id": "999" } }""")]
    [InlineData("""{ "info": { "tmdb_id": "555" }, "movie_data": { "name": "[XXX] Movie", "category_id": "10" } }""")]
    [InlineData("""{ "info": {}, "movie_data": { "name": "Movie", "category_id": "10" } }""")]
    public void TransformInfo_WhenItemWouldNotBeListed_ReturnsNull(string json)
    {
        var source = CreateSource(itemRules: new ItemRule
        {
            Sequence = 1,
            Action = RuleAction.Exclude,
            Operator = RuleOperator.StartsWith,
            Pattern = "[XXX]"
        });

        var result = TransformInfo(json, ContentType.Vod, source);

        Assert.Null(result);
    }

    [Fact]
    public void EnrichStreamItem_WhenTmdbInfoIsLoaded_ReplacesOnlyExistingFields()
    {
        var item = ParseObject("""{ "stream_id": 1, "name": "Matrix (1999)", "year": 1990, "rating": "5", "rating_5based": 2.5, "stream_icon": "http://provider/icon.jpg", "tmdb_id": "603" }""");

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(CreateLoadedInfo()), CreateSource());

        Assert.True(included);
        Assert.Equal("Matrix | 1999", item["name"]?.GetValue<string>());
        Assert.Equal(1999, item["year"]?.GetValue<double>());
        Assert.Equal("8.2", item["rating"]?.GetValue<string>());
        Assert.Equal(4.1, item["rating_5based"]?.GetValue<double>());
        Assert.Equal("https://image.tmdb.org/t/p/w342/matrix.jpg", item["stream_icon"]?.GetValue<string>());
        Assert.False(item.ContainsKey("plot"));
        Assert.False(item.ContainsKey("cover"));
        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    [Fact]
    public void EnrichStreamItem_WhenTmdbInfoIsUnknown_ExcludesItemAndEnqueuesLoad()
    {
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": 603 }""");

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(), CreateSource());

        Assert.False(included);
        Assert.False(_tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(ContentType.Vod, TmdbId)));
    }

    [Fact]
    public void EnrichStreamItem_WhenTmdbInfoIsDueForRefresh_EnrichesIncludesAndEnqueuesLoad()
    {
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "603" }""");
        var info = CreateLoadedInfo();
        info.NextLoadAtUtc = _time.Now;

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(info), CreateSource());

        Assert.True(included);
        Assert.Equal("Matrix | 1999", item["name"]?.GetValue<string>());
        Assert.Equal(1, _tmdbInfoQueue.Count);
    }

    [Fact]
    public void EnrichStreamItem_WhenTmdbInfoLoadIsDeferred_ExcludesItemWithoutEnqueuing()
    {
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "603" }""");
        var info = new TmdbInfo { TmdbId = TmdbId, ContentType = ContentType.Vod, LoadAttemptCount = 1, NextLoadAtUtc = _time.Now.AddDays(1) };

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(info), CreateSource());

        Assert.False(included);
        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    [Theory]
    [InlineData("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "abc" }""")]
    [InlineData("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "-3" }""")]
    public void EnrichStreamItem_WhenTmdbIdIsInvalid_ExcludesItemWithoutEnqueuing(string json)
    {
        var item = ParseObject(json);

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(CreateLoadedInfo()), CreateSource());

        Assert.False(included);
        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    [Fact]
    public void EnrichStreamItem_WithoutTmdbApiKey_DoesNotEnqueueLoad()
    {
        var service = CreateService(tmdbApiKey: "");
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "603" }""");

        service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(), CreateSource());

        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    [Fact]
    public void EnrichStreamItem_DoesNotApplyItemRulesAgainOnEnrichedItem()
    {
        var source = CreateSource(itemRules: new ItemRule { Sequence = 1, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "| 1999" });
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "603" }""");

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(CreateLoadedInfo()), source);

        Assert.True(included);
    }

    [Theory]
    [InlineData(TmdbRuleField.Title, RuleOperator.Contains, "Matr")]
    [InlineData(TmdbRuleField.Genre, RuleOperator.Contains, "horror")]
    public void EnrichStreamItem_WhenTmdbRuleExcludes_ExcludesItemWithoutEnrichingIt(TmdbRuleField field, RuleOperator @operator, string pattern)
    {
        var source = CreateSource(tmdbRules: [new TmdbRule { Sequence = 10, Field = field, Action = RuleAction.Exclude, Operator = @operator, Pattern = pattern }]);
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "603" }""");
        var info = CreateLoadedInfo();
        info.Genres = ["Action", "Horror"];

        var included = _service.EnrichStreamItem(item, ContentType.Vod, CreateTmdbInfos(info), source);

        Assert.False(included);
        Assert.Equal("Movie", item["name"]?.GetValue<string>());
    }

    [Fact]
    public void EnrichStreamItem_WhenTmdbInfoIsExcludedManually_ExcludesItemWithoutEnqueuing()
    {
        var item = ParseObject("""{ "stream_id": 1, "name": "Movie", "tmdb_id": "603" }""");
        var tmdbInfos = new TmdbInfoLookup(new Dictionary<long, TmdbInfo>(), new HashSet<long> { TmdbId });

        var included = _service.EnrichStreamItem(item, ContentType.Vod, tmdbInfos, CreateSource());

        Assert.False(included);
        Assert.Equal(0, _tmdbInfoQueue.Count);
    }

    [Fact]
    public void EnrichInfo_ForVod_EnrichesInfoAndMovieDataSections()
    {
        var payload = ParseObject("""{ "info": { "name": "Movie", "plot": "Provider plot", "cover_big": "", "releasedate": "1990-01-01", "tmdb_id": "603" }, "movie_data": { "name": "Movie" } }""");

        var included = _service.EnrichInfo(payload, ContentType.Vod, TmdbId, CreateTmdbInfos(CreateLoadedInfo()), CreateSource());

        Assert.True(included);
        Assert.Equal("Matrix | 1999", payload["info"]?["name"]?.GetValue<string>());
        Assert.Equal("Neo discovers the truth.", payload["info"]?["plot"]?.GetValue<string>());
        Assert.Equal("https://image.tmdb.org/t/p/w780/matrix.jpg", payload["info"]?["cover_big"]?.GetValue<string>());
        Assert.Equal("1999-03-30", payload["info"]?["releasedate"]?.GetValue<string>());
        Assert.Equal("Matrix | 1999", payload["movie_data"]?["name"]?.GetValue<string>());
    }

    [Fact]
    public void EnrichInfo_WhenTmdbRuleExcludes_ExcludesItem()
    {
        var source = CreateSource(tmdbRules: [new TmdbRule { Sequence = 10, Field = TmdbRuleField.Title, Action = RuleAction.Exclude, Operator = RuleOperator.Contains, Pattern = "Matrix" }]);
        var payload = ParseObject("""{ "seasons": [], "info": { "name": "Show", "tmdb_id": "603" }, "episodes": {} }""");

        var included = _service.EnrichInfo(payload, ContentType.Series, TmdbId, CreateTmdbInfos(CreateLoadedInfo(ContentType.Series)), source);

        Assert.False(included);
    }

    [Fact]
    public void EnrichInfo_WhenTmdbInfoIsNotLoaded_ExcludesItemAndEnqueuesLoad()
    {
        var payload = ParseObject("""{ "info": { "name": "Movie", "tmdb_id": "603" }, "movie_data": { "name": "Movie" } }""");

        var included = _service.EnrichInfo(payload, ContentType.Vod, TmdbId, CreateTmdbInfos(), CreateSource());

        Assert.False(included);
        Assert.Equal(1, _tmdbInfoQueue.Count);
    }

    [Fact]
    public void EnrichInfo_WithoutTmdbId_ExcludesItem()
    {
        var payload = ParseObject("""{ "info": { "name": "Movie" }, "movie_data": { "name": "Movie" } }""");

        var included = _service.EnrichInfo(payload, ContentType.Vod, tmdbId: null, CreateTmdbInfos(CreateLoadedInfo()), CreateSource());

        Assert.False(included);
    }

    [Theory]
    [InlineData("""{ "info": { "tmdb_id": "603" }, "movie_data": { "tmdb_id": "1" } }""", ContentType.Vod, 603L)]
    [InlineData("""{ "info": [], "movie_data": { "tmdb_id": 604 } }""", ContentType.Vod, 604L)]
    [InlineData("""{ "info": {}, "movie_data": { "tmdb_id": 604 } }""", ContentType.Series, null)]
    [InlineData("""{ "info": { "tmdb_id": "" } }""", ContentType.Series, null)]
    public void ReadInfoTmdbId_ReadsInfoThenMovieDataForVod(string json, ContentType contentType, long? expectedTmdbId)
    {
        Assert.Equal(expectedTmdbId, ItemService.ReadInfoTmdbId(ParseObject(json), contentType));
    }

    private TmdbInfo CreateLoadedInfo(ContentType contentType = ContentType.Vod) => new()
    {
        TmdbId = TmdbId,
        ContentType = contentType,
        Title = "Matrix",
        ReleaseDate = new DateOnly(1999, 3, 30),
        PosterPath = "/matrix.jpg",
        Overview = "Neo discovers the truth.",
        VoteAverage = 8.2,
        VoteCount = 26000,
        LoadedAtUtc = _time.Now,
        NextLoadAtUtc = _time.Now.AddDays(60)
    };

    private static TmdbInfoLookup CreateTmdbInfos(params TmdbInfo[] infos) => new(infos.ToDictionary(info => info.TmdbId), new HashSet<long>());

    private static JsonObject ParseObject(string json) => Assert.IsType<JsonObject>(JsonNode.Parse(json));

    private async Task<TmdbIdRetrieverRequest> ReadSingleQueuedRequestAsync()
    {
        await using var requests = _queue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await requests.MoveNextAsync());
        return requests.Current;
    }

    private ItemService CreateService(string tmdbApiKey)
        => new(_queue, _tmdbInfoQueue, Options.Create(new TmdbOptions { ApiKey = tmdbApiKey }), _time);

    private JsonObject? Transform(string json, XtreamContext context, HashSet<string> seenIds, XtreamSourceSnapshot? source = null)
        => _service.TransformStreamItem(JsonNode.Parse(json), context, source ?? CreateSource(), XtreamCategoryIdMapping, seenIds);

    private static JsonObject? TransformInfo(string json, ContentType contentType, XtreamSourceSnapshot source)
        => ItemService.TransformInfo(JsonNode.Parse(json), "1", contentType, source, XtreamCategoryIdMapping);

    private static XtreamSourceSnapshot CreateSource(
        IReadOnlyDictionary<string, long>? streamTmdbMappings = null,
        HashSet<string>? deferredTmdbLookups = null,
        IReadOnlyList<TmdbRule>? tmdbRules = null,
        params ItemRule[] itemRules)
        => new(SourceId, [], itemRules, streamTmdbMappings ?? new Dictionary<string, long>(), deferredTmdbLookups ?? [], tmdbRules ?? []);

    private static XtreamContext CreateContext(ContentType contentType, string queryString = "")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = new QueryString(queryString);
        return new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext, RequestAction.GetItems, contentType);
    }
}
