using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

public sealed class XtreamRoutesTests : IAsyncDisposable
{
    private const string ProviderPrefix = "/http/provider.example.com/8080";

    // requests without action (streams) and unhandled player_api actions are answered by the upstream stub
    private readonly StubXtreamHttpClientFactory _upstream = new(new Dictionary<string, (HttpStatusCode, string)>
    {
        [""] = (HttpStatusCode.OK, "stream content"),
        ["get_live_streams"] = (HttpStatusCode.OK, "[]"),
        ["redirect"] = (HttpStatusCode.Found, string.Empty)
    });

    private readonly WatchHistoryQueue _watchHistoryQueue = new(TimeProvider.System);
    private readonly XtreamAccountDirectory _accountDirectory = new();

    private WebApplication? _app;
    private int _dbContextCount;

    [Fact]
    public async Task StreamRequest_IsForwardedWithoutCreatingDbContext()
    {
        var client = await StartAsync();

        using var response = await client.GetAsync($"{ProviderPrefix}/movie/user/secret/1.mp4", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stream content", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/movie/user/secret/1.mp4", Assert.Single(_upstream.RequestedUris).AbsolutePath);
        Assert.Equal(0, _dbContextCount);
    }

    [Fact]
    public async Task MovieStreamRequest_EnqueuesThePlaybackForTheWatchHistory()
    {
        var client = await StartAsync();

        using var response = await client.GetAsync($"{ProviderPrefix}/movie/user/secret/1.mp4", TestContext.Current.CancellationToken);
        using var rangeResponse = await client.GetAsync($"{ProviderPrefix}/movie/user/secret/1.mp4", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _watchHistoryQueue.Count);
        await using var requests = _watchHistoryQueue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await requests.MoveNextAsync());
        Assert.Equal(
            ("http", "provider.example.com", 8080, new XtreamMovieStream("user", "secret", "1")),
            (requests.Current.Protocol, requests.Current.Host, requests.Current.Port, requests.Current.Movie));
    }

    [Theory]
    [InlineData("GET", "/series/user/secret/1.mp4")]
    [InlineData("GET", "/live/user/secret/1.ts")]
    [InlineData("GET", "/player_api.php?action=get_live_streams")]
    [InlineData("HEAD", "/movie/user/secret/1.mp4")]
    public async Task OtherForwardedRequest_DoesNotEnqueueAPlayback(string method, string pathAndQuery)
    {
        var client = await StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), ProviderPrefix + pathAndQuery);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, _watchHistoryQueue.Count);
    }

    [Fact]
    public async Task MovieStreamRequest_RedirectedByTheProvider_EnqueuesThePlayback()
    {
        var client = await StartAsync();

        // Xtream panels often redirect a stream to a load balancer, which the client then calls directly
        using var response = await client.GetAsync($"{ProviderPrefix}/movie/user/secret/1.mp4?action=redirect", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(1, _watchHistoryQueue.Count);
    }

    [Fact]
    public async Task MovieStreamRequest_WhenTheProviderFails_DoesNotEnqueueAPlayback()
    {
        var client = await StartAsync();

        // the stub answers 404 to the unknown action
        using var response = await client.GetAsync($"{ProviderPrefix}/movie/user/secret/1.mp4?action=unknown", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _watchHistoryQueue.Count);
    }

    [Theory]
    [InlineData("/player_api.php?action=get_vod_streams")]
    [InlineData("/PLAYER_API.PHP?action=get_vod_streams")]
    [InlineData("/player_api.php/?action=get_vod_streams")]
    public async Task PlayerApiRequest_IsHandledByXtreamForge(string pathAndQuery)
    {
        var client = await StartAsync();

        // the source is unknown: ItemsGetEndpoint rejects the request without calling upstream
        using var response = await client.GetAsync(ProviderPrefix + pathAndQuery, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_upstream.RequestedUris);
    }

    [Fact]
    public async Task PlayerApiRequest_WithUnhandledAction_IsForwarded()
    {
        var client = await StartAsync();

        using var response = await client.GetAsync($"{ProviderPrefix}/player_api.php?action=get_live_streams", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("/player_api.php", Assert.Single(_upstream.RequestedUris).AbsolutePath);
    }

    [Theory]
    [InlineData("/http/other.example.com/8080/movie/user/secret/1.mp4")]
    [InlineData("/http/other.example.com/8080/player_api.php?action=get_vod_streams")]
    public async Task RequestToNotAllowedHost_IsRejectedOnBothRoutes(string pathAndQuery)
    {
        var client = await StartAsync();

        using var response = await client.GetAsync(pathAndQuery, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_upstream.RequestedUris);
    }

    [Theory]
    [InlineData("/movie/user/secret/1.mp4")]
    [InlineData("/series/user/secret/2.mkv")]
    [InlineData("/live/user/secret/3.ts")]
    public async Task StreamRequestWithoutDestination_IsForwardedToTheUpstreamOfTheAccount(string path)
    {
        _accountDirectory.Remember("user", "secret", new XtreamUpstream("http", "provider.example.com", 8080));
        var client = await StartAsync();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("stream content", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal($"http://provider.example.com:8080{path}", Assert.Single(_upstream.RequestedUris).AbsoluteUri);
        Assert.Equal(0, _dbContextCount);
    }

    [Fact]
    public async Task MovieStreamRequestWithoutDestination_EnqueuesThePlayback()
    {
        _accountDirectory.Remember("user", "secret", new XtreamUpstream("http", "provider.example.com", 8080));
        var client = await StartAsync();

        using var response = await client.GetAsync("/movie/user/secret/1.mp4", TestContext.Current.CancellationToken);

        Assert.Equal(1, _watchHistoryQueue.Count);
    }

    [Fact]
    public async Task StreamRequestWithoutDestination_ForAnUnknownAccount_ReturnsNotFoundWithoutCallingUpstream()
    {
        _accountDirectory.Remember("user", "secret", new XtreamUpstream("http", "provider.example.com", 8080));
        var client = await StartAsync();

        using var response = await client.GetAsync("/movie/user/other/1.mp4", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_upstream.RequestedUris);
    }

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(Options.Create(new XtreamProxyOptions { AllowedHosts = ["provider.example.com"] }));
        builder.Services.AddSingleton<IHttpClientFactory>(_upstream);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped(_ =>
        {
            Interlocked.Increment(ref _dbContextCount);
            return SqliteDbContextFactory.Create();
        });

        builder.Services.AddSingleton<XtreamProviderValidator>();
        builder.Services.AddSingleton<XtreamContextBuilder>();
        builder.Services.AddSingleton(_accountDirectory);
        builder.Services.AddSingleton<TmdbIdRetrieverQueue>();
        builder.Services.AddSingleton<TmdbInfoQueue>();
        builder.Services.AddSingleton(_watchHistoryQueue);
        builder.Services.AddSingleton<TmdbClient>();
        builder.Services.AddScoped<SourceService>();
        builder.Services.AddScoped<CategoryService>();
        builder.Services.AddScoped<ItemService>();
        builder.Services.AddScoped<TmdbInfoService>();
        builder.Services.AddScoped<AuthenticateEndpoint>();
        builder.Services.AddScoped<CategoriesGetEndpoint>();
        builder.Services.AddScoped<ItemsGetEndpoint>();
        builder.Services.AddScoped<ItemGetEndpoint>();
        builder.Services.AddScoped<XtreamRequestForwardEndpoint>();

        _app = builder.Build();
        _app.MapXtreamEndpoints();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        return _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
