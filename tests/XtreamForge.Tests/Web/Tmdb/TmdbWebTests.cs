using System.Net;
using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Rules;
using XtreamForge.Web.Features.Tmdb;

namespace XtreamForge.Tests.Web.Tmdb;

public class TmdbWebTests
{
    [Fact]
    public void TmdbInfoFilter_WithoutFilter_RequestsOnlyTheContentTypeAndThePage()
    {
        Assert.Equal("contentType=Vod&skip=0&take=100", new TmdbInfoFilter().ToQueryString(ContentType.Vod, 0, 100));
    }

    [Fact]
    public void TmdbInfoFilter_SendsEveryFilter()
    {
        var filter = new TmdbInfoFilter(" Star & Wars ", DecisionFilter.Excluded, ManualExclusionFilter.NotExcluded, LoadStateFilter.Loaded, "Science Fiction");

        Assert.Equal(
            "contentType=Series&skip=200&take=50&search=Star%20%26%20Wars&genre=Science%20Fiction&decision=Exclude&isExcluded=false&isLoaded=true",
            filter.ToQueryString(ContentType.Series, 200, 50));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void TmdbInfoFilter_WithoutGenre_DoesNotSendTheGenre(string? genre)
    {
        Assert.Equal("contentType=Vod&skip=0&take=10", new TmdbInfoFilter(Genre: genre).ToQueryString(ContentType.Vod, 0, 10));
    }

    [Theory]
    [InlineData(DecisionFilter.Included, ManualExclusionFilter.Excluded, LoadStateFilter.NotLoaded, "decision=Include&isExcluded=true&isLoaded=false")]
    [InlineData(DecisionFilter.All, ManualExclusionFilter.All, LoadStateFilter.All, "")]
    public void TmdbInfoFilter_MapsTheFilterValues(DecisionFilter decision, ManualExclusionFilter manualExclusion, LoadStateFilter loadState, string expectedFilters)
    {
        var query = new TmdbInfoFilter(null, decision, manualExclusion, loadState).ToQueryString(ContentType.Vod, 0, 10);

        Assert.Equal(expectedFilters, string.Join('&', query.Split('&').Skip(3)));
    }

    [Fact]
    public void TmdbInfoFilter_FromLink_WithoutFilterParameter_ReturnsNull()
    {
        Assert.Null(TmdbInfoFilter.FromLink("Matrix", null, null, null));
    }

    [Fact]
    public void TmdbInfoFilter_FromLink_SetsTheLinkedFiltersAndResetsTheOthers()
    {
        Assert.Equal(new TmdbInfoFilter(null, LoadState: LoadStateFilter.NotLoaded), TmdbInfoFilter.FromLink(null, null, null, "NotLoaded"));
        Assert.Equal(new TmdbInfoFilter("Shrek", ManualExclusion: ManualExclusionFilter.Excluded), TmdbInfoFilter.FromLink("Shrek", null, "excluded", null));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("42")]
    [InlineData("")]
    public void TmdbInfoFilter_FromLink_IgnoresInvalidValues(string value)
    {
        Assert.Equal(new TmdbInfoFilter(), TmdbInfoFilter.FromLink(null, value, value, value));
    }

    [Theory]
    [InlineData(RuleKind.Category, null, "name")]
    [InlineData(RuleKind.Item, TmdbRuleField.Genre, "name")]
    [InlineData(RuleKind.Tmdb, null, "TMDB title")]
    [InlineData(RuleKind.Tmdb, TmdbRuleField.Title, "TMDB title")]
    [InlineData(RuleKind.Tmdb, TmdbRuleField.Genre, "TMDB genre")]
    public void RuleKindTexts_FieldName_DependsOnTheKindAndTheField(RuleKind kind, TmdbRuleField? field, string expected)
    {
        Assert.Equal(expected, RuleKindTexts.FieldName(kind, field));
    }

    [Theory]
    [InlineData(ContentType.Vod, "https://www.themoviedb.org/movie/603")]
    [InlineData(ContentType.Series, "https://www.themoviedb.org/tv/603")]
    public void TmdbInfoDto_LinksToTheTmdbPage(ContentType contentType, string expectedUrl)
    {
        var info = new TmdbInfoDto(1, 603, contentType, null, null, null, null, null, [], null, null, false, null, 0, DateTimeOffset.UtcNow, InclusionDecision.Include, null, null);

        Assert.Equal(expectedUrl, info.TmdbUrl);
        Assert.Equal("TMDB #603", info.DisplayTitle);
    }

    [Fact]
    public void TmdbMappingFilter_SendsEveryFilter()
    {
        Assert.Equal("contentType=Vod&skip=0&take=100", new TmdbMappingFilter().ToQueryString(ContentType.Vod, 0, 100));
        Assert.Equal(
            "contentType=Series&skip=200&take=50&search=Star%20%26%20Wars&isMapped=false",
            new TmdbMappingFilter(" Star & Wars ", MappingStateFilter.NotMapped).ToQueryString(ContentType.Series, 200, 50));
        Assert.EndsWith("&isMapped=true", new TmdbMappingFilter(State: MappingStateFilter.Mapped).ToQueryString(ContentType.Vod, 0, 10), StringComparison.Ordinal);
    }

    [Fact]
    public void TmdbMappingDto_LinksToTheTmdbPageOnlyWhenMapped()
    {
        var mapped = new TmdbMappingDto(1, ContentType.Series, "42", 603, 0, null, null, null, null, null);

        Assert.Equal("https://www.themoviedb.org/tv/603", mapped.TmdbUrl);
        Assert.Null((mapped with { TmdbId = null }).TmdbUrl);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent, AdminOperationResult.Success)]
    [InlineData(HttpStatusCode.NotFound, AdminOperationResult.NotFound)]
    [InlineData(HttpStatusCode.BadRequest, AdminOperationResult.Invalid)]
    public async Task TmdbMappingsClient_SetTmdbIdAsync_PatchesTheMapping(HttpStatusCode statusCode, AdminOperationResult expected)
    {
        var handler = new RecordingHandler(statusCode, string.Empty);
        var client = new TmdbMappingsClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") });

        var result = await client.SetTmdbIdAsync(7, 603, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal((HttpMethod.Patch, "/api/admin/tmdb-mappings/7", """{"tmdbId":603}"""), (handler.Method, handler.PathAndQuery, handler.Body));
    }

    [Fact]
    public async Task TmdbMappingsClient_GetPageAsync_WhenSourceDoesNotExist_ReturnsNull()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, string.Empty);
        var client = new TmdbMappingsClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") });

        var page = await client.GetPageAsync(3, ContentType.Vod, new TmdbMappingFilter(), 0, 10, TestContext.Current.CancellationToken);

        Assert.Null(page);
        Assert.Equal("/api/admin/sources/3/tmdb-mappings?contentType=Vod&skip=0&take=10", handler.PathAndQuery);
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode) { Content = new StringContent(content) };
        }
    }
}
