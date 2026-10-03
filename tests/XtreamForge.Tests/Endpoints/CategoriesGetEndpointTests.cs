using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

public class CategoriesGetEndpointTests : IAsyncDisposable
{
    private const string UpstreamCategories = """[ { "category_id": "10", "category_name": "Action" } ]""";

    private readonly XtreamForgeDbContext _dbContext;
    private readonly TmdbIdCache _cache = new(TimeProvider.System);

    public CategoriesGetEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task GetAsync_ForVod_ReturnsTheRecommendationsAndPopularCategoriesFirst()
    {
        var context = CreateContext(ContentType.Vod, "get_vod_categories");

        await CreateEndpoint("get_vod_categories", new RecommendationOptions { CategoryName = "Recommandations", CategoryId = 777 }, new PopularOptions { CategoryName = "POPULAIRES", CategoryId = 778 })
            .GetAsync(context, TestContext.Current.CancellationToken);

        var categories = ReadResponse(context);
        Assert.Equal(
            [("777", "Recommandations"), ("778", "POPULAIRES")],
            categories.Take(2).Select(category => (category["category_id"]?.GetValue<string>(), category["category_name"]?.GetValue<string>())));
        Assert.Equal(["Recommandations", "POPULAIRES", "Action"], categories.Select(category => category["category_name"]?.GetValue<string>()));
    }

    [Fact]
    public async Task GetAsync_ForSeries_ReturnsThePopularCategoryWithoutTheRecommendationsCategory()
    {
        var context = CreateContext(ContentType.Series, "get_series_categories");

        await CreateEndpoint("get_series_categories", new RecommendationOptions(), new PopularOptions { CategoryName = "POPULAIRES" })
            .GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["POPULAIRES", "Action"], ReadResponse(context).Select(category => category["category_name"]?.GetValue<string>()));
    }

    [Fact]
    public async Task GetAsync_WithoutCategoryNames_ReturnsNoVirtualCategory()
    {
        var context = CreateContext(ContentType.Vod, "get_vod_categories");

        await CreateEndpoint("get_vod_categories", new RecommendationOptions { CategoryName = "" }, new PopularOptions { CategoryName = "" })
            .GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(["Action"], ReadResponse(context).Select(category => category["category_name"]?.GetValue<string>()));
    }

    private CategoriesGetEndpoint CreateEndpoint(string action, RecommendationOptions recommendationOptions, PopularOptions popularOptions)
        => new(
            new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)> { [action] = (HttpStatusCode.OK, UpstreamCategories) }),
            new CategoryService(_dbContext),
            new StubTmdbHttpClientFactory(_ => null).CreateVirtualCategoryService(_dbContext, _cache, recommendationOptions, popularOptions),
            NullLogger<CategoriesGetEndpoint>.Instance);

    private static List<JsonObject> ReadResponse(XtreamContext context)
    {
        var body = (MemoryStream)context.Response.Body;
        var categories = Assert.IsType<JsonArray>(JsonNode.Parse(body.ToArray()));

        return [.. categories.Select(category => Assert.IsType<JsonObject>(category))];
    }

    private static XtreamContext CreateContext(ContentType contentType, string action)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.QueryString = new QueryString($"?action={action}");
        httpContext.Response.Body = new MemoryStream();
        return new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext, RequestAction.GetCategories, contentType);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        _cache.Dispose();
        GC.SuppressFinalize(this);
    }
}
