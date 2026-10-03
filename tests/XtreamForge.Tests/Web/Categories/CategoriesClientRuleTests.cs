using System.Net;
using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Tests.Web.Categories;

public class CategoriesClientRuleTests
{
    private static readonly RuleRequest Request = new(ContentType.Vod, 10, RuleAction.Exclude, RuleOperator.Contains, "x", false, true);

    [Theory]
    [InlineData(RuleKind.Category, "/api/admin/sources/3/category-rules?contentType=Vod")]
    [InlineData(RuleKind.Item, "/api/admin/sources/3/item-rules?contentType=Vod")]
    [InlineData(RuleKind.Tmdb, "/api/admin/tmdb-rules?contentType=Vod")]
    public async Task GetRulesAsync_UsesEndpointOfKind(RuleKind kind, string expectedPathAndQuery)
    {
        var handler = new RecordingHandler("[]");
        var client = CreateClient(handler);

        await client.GetRulesAsync(kind, 3, ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(expectedPathAndQuery, handler.PathAndQuery);
    }

    [Theory]
    [InlineData(RuleKind.Category, "/api/admin/sources/3/category-rules")]
    [InlineData(RuleKind.Item, "/api/admin/sources/3/item-rules")]
    [InlineData(RuleKind.Tmdb, "/api/admin/tmdb-rules")]
    public async Task CreateRuleAsync_UsesEndpointOfKind(RuleKind kind, string expectedPath)
    {
        var handler = new RecordingHandler("{}");
        var client = CreateClient(handler);

        await client.CreateRuleAsync(kind, 3, Request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(expectedPath, handler.PathAndQuery);
    }

    [Theory]
    [InlineData(RuleKind.Category, "/api/admin/category-rules/7")]
    [InlineData(RuleKind.Item, "/api/admin/item-rules/7")]
    [InlineData(RuleKind.Tmdb, "/api/admin/tmdb-rules/7")]
    public async Task UpdateAndDeleteRuleAsync_UseEndpointOfKind(RuleKind kind, string expectedPath)
    {
        var handler = new RecordingHandler(string.Empty);
        var client = CreateClient(handler);

        await client.UpdateRuleAsync(kind, 7, Request, TestContext.Current.CancellationToken);
        Assert.Equal((HttpMethod.Put, expectedPath), (handler.Method, handler.PathAndQuery));

        await client.DeleteRuleAsync(kind, 7, TestContext.Current.CancellationToken);
        Assert.Equal((HttpMethod.Delete, expectedPath), (handler.Method, handler.PathAndQuery));
    }

    [Theory]
    [InlineData(RuleKind.Category, "/api/admin/sources/3/category-rules/order")]
    [InlineData(RuleKind.Item, "/api/admin/sources/3/item-rules/order")]
    [InlineData(RuleKind.Tmdb, "/api/admin/tmdb-rules/order")]
    public async Task ReorderRulesAsync_UsesEndpointOfKind(RuleKind kind, string expectedPath)
    {
        var handler = new RecordingHandler("[]");
        var client = CreateClient(handler);

        await client.ReorderRulesAsync(kind, 3, new RuleOrderRequest(ContentType.Vod, [2, 1]), TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal(expectedPath, handler.PathAndQuery);
    }

    [Fact]
    public async Task TmdbRules_IgnoreTheSource()
    {
        var handler = new RecordingHandler("[]");
        var client = CreateClient(handler);

        await client.GetRulesAsync(RuleKind.Tmdb, null, ContentType.Series, TestContext.Current.CancellationToken);

        Assert.Equal("/api/admin/tmdb-rules?contentType=Series", handler.PathAndQuery);
    }

    [Theory]
    [InlineData(RuleKind.Category)]
    [InlineData(RuleKind.Item)]
    public async Task SourceRules_WithoutSource_Throw(RuleKind kind)
    {
        var client = CreateClient(new RecordingHandler("[]"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetRulesAsync(kind, null, ContentType.Vod, TestContext.Current.CancellationToken));
    }

    private static CategoriesClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") });

    private sealed class RecordingHandler(string content) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
