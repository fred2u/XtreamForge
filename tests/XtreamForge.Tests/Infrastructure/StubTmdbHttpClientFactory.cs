using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.ApiService.Services.VirtualCategories;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Services.Tmdb.Scoring;
using XtreamForge.Database;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// Answers TMDB requests through <c>respond</c> (404 when it returns null) and records the requested URIs.
/// </summary>
public sealed class StubTmdbHttpClientFactory : IHttpClientFactory
{
    public static readonly Uri BaseAddress = new("https://tmdb.example.com/3/");

    private readonly Func<Uri, string?> _respond;

    public StubTmdbHttpClientFactory(Func<Uri, string?> respond)
    {
        _respond = respond;
    }

    /// <summary>
    /// Responds with the content registered for the relative path without query, for example "movie/603".
    /// </summary>
    public StubTmdbHttpClientFactory(IReadOnlyDictionary<string, string> responsesByPath)
        : this(uri => responsesByPath.GetValueOrDefault(GetRelativePath(uri)))
    {
    }

    public List<Uri> RequestedUris { get; } = [];

    public static string GetRelativePath(Uri uri) => BaseAddress.MakeRelativeUri(uri).OriginalString.Split('?')[0];

    public HttpClient CreateClient(string name) => new(new StubHandler(this)) { BaseAddress = BaseAddress };

    public TmdbClient CreateTmdbClient() => new(this, CreateOptions("token", 85));

    // without an api key, RecommendationService does not call TMDB
    public RecommendationService CreateRecommendationService(XtreamForgeDbContext dbContext, TmdbIdCache cache, string apiKey = "token")
    {
        var options = CreateOptions(apiKey, 85);

        return new RecommendationService(dbContext, new TmdbClient(this, options), cache, options, NullLogger<RecommendationService>.Instance);
    }

    // without an api key, PopularService does not call TMDB
    public PopularService CreatePopularService(TmdbIdCache cache, string apiKey = "token")
    {
        var options = CreateOptions(apiKey, 85);

        return new PopularService(new TmdbClient(this, options), cache, options, NullLogger<PopularService>.Instance);
    }

    // recommendations and popular titles both answered by this stub, with the default category names and IDs unless given
    public VirtualCategoryService CreateVirtualCategoryService(
        XtreamForgeDbContext dbContext,
        TmdbIdCache cache,
        RecommendationOptions? recommendationOptions = null,
        PopularOptions? popularOptions = null)
        => new(
            CreateRecommendationService(dbContext, cache),
            CreatePopularService(cache),
            Options.Create(recommendationOptions ?? new RecommendationOptions()),
            Options.Create(popularOptions ?? new PopularOptions()));

    // without an api key, TmdbInfoService does not call TMDB
    public TmdbInfoService CreateTmdbInfoService(XtreamForgeDbContext dbContext, TimeProvider timeProvider, string apiKey = "token")
    {
        var options = CreateOptions(apiKey, 85);

        return new TmdbInfoService(dbContext, new TmdbClient(this, options), options, timeProvider);
    }

    public TmdbIdMatcher CreateMatcher(string apiKey = "token", int minimumConfidenceScore = 85)
    {
        var options = CreateOptions(apiKey, minimumConfidenceScore);
        var tmdbClient = new TmdbClient(this, options);
        ITmdbScoringRule[] rules =
        [
            new TitleScoringRule(),
            new PosterScoringRule(),
            new ReleaseDateScoringRule(),
            new CastScoringRule(),
            new GenreScoringRule(),
            new SingleCandidateScoringRule(),
            new SeasonScoringRule(tmdbClient)
        ];

        return new TmdbIdMatcher(tmdbClient, rules, options);
    }

    private static IOptions<TmdbOptions> CreateOptions(string apiKey, int minimumConfidenceScore)
        => Options.Create(new TmdbOptions
        {
            BaseUrl = BaseAddress.AbsoluteUri,
            ApiKey = apiKey,
            PreferredLanguage = "fr-FR",
            MinimumConfidenceScore = minimumConfidenceScore
        });

    private sealed class StubHandler(StubTmdbHttpClientFactory factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            factory.RequestedUris.Add(uri);

            var content = factory._respond(uri);

            return Task.FromResult(content is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
}
