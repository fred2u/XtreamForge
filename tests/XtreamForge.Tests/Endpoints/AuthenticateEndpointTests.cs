using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

public class AuthenticateEndpointTests
{
    private const string UpstreamAuthentication = """
        {
          "user_info": { "username": "user", "auth": 1, "status": "Active" },
          "server_info": { "url": "provider.example.com", "port": "8080", "https_port": "8443", "server_protocol": "http", "rtmp_port": "25462" }
        }
        """;

    private readonly XtreamAccountDirectory _accountDirectory = new();

    [Fact]
    public async Task GetAsync_RewritesServerInfoToXtreamForgeAndKeepsTheRest()
    {
        var context = CreateContext("https", new HostString("forge.example.com", 5443));

        await CreateEndpoint(UpstreamAuthentication).GetAsync(context, TestContext.Current.CancellationToken);

        var payload = ReadResponse(context);
        var serverInfo = Assert.IsType<JsonObject>(payload["server_info"]);
        Assert.Equal(
            ("forge.example.com", "5443", "5443", "https", "25462"),
            (serverInfo["url"]?.ToString(), serverInfo["port"]?.ToString(), serverInfo["https_port"]?.ToString(), serverInfo["server_protocol"]?.ToString(), serverInfo["rtmp_port"]?.ToString()));
        Assert.Equal("Active", payload["user_info"]?["status"]?.ToString());
    }

    [Theory]
    [InlineData("http", 80)]
    [InlineData("https", 443)]
    public async Task GetAsync_WithoutPortInTheHost_UsesTheDefaultPortOfTheScheme(string scheme, int expectedPort)
    {
        var context = CreateContext(scheme, new HostString("forge.example.com"));

        await CreateEndpoint(UpstreamAuthentication).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expectedPort.ToString(), ReadResponse(context)["server_info"]?["port"]?.ToString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("\"1\"")]
    public async Task GetAsync_WhenAuthenticated_RemembersTheUpstreamOfTheAccount(string auth)
    {
        var context = CreateContext("http", new HostString("forge.example.com", 5000));

        await CreateEndpoint($$"""{ "user_info": { "auth": {{auth}} }, "server_info": {} }""").GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(new XtreamUpstream("http", "provider.example.com", 8080), _accountDirectory.Find("user", "secret"));
    }

    [Fact]
    public async Task GetAsync_WhenNotAuthenticated_ReturnsThePayloadUnchangedAndRemembersNothing()
    {
        const string upstream = """{ "user_info": { "auth": 0 }, "server_info": { "url": "provider.example.com" } }""";
        var context = CreateContext("http", new HostString("forge.example.com", 5000));

        await CreateEndpoint(upstream).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal("provider.example.com", ReadResponse(context)["server_info"]?["url"]?.ToString());
        Assert.Null(_accountDirectory.Find("user", "secret"));
    }

    [Fact]
    public async Task GetAsync_ForwardsTheRequestWithItsQueryString()
    {
        var httpClientFactory = CreateHttpClientFactory(HttpStatusCode.OK, UpstreamAuthentication);

        await CreateEndpoint(httpClientFactory).GetAsync(CreateContext("http", new HostString("forge.example.com")), TestContext.Current.CancellationToken);

        Assert.Equal("http://provider.example.com:8080/player_api.php?username=user&password=secret", Assert.Single(httpClientFactory.RequestedUris).AbsoluteUri);
    }

    [Fact]
    public async Task GetAsync_WhenUpstreamFails_ForwardsTheStatus()
    {
        var context = CreateContext("http", new HostString("forge.example.com"));

        await CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.Forbidden, "denied")).GetAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Null(_accountDirectory.Find("user", "secret"));
    }

    private AuthenticateEndpoint CreateEndpoint(string upstreamContent)
        => CreateEndpoint(CreateHttpClientFactory(HttpStatusCode.OK, upstreamContent));

    private AuthenticateEndpoint CreateEndpoint(StubXtreamHttpClientFactory httpClientFactory)
        => new(httpClientFactory, _accountDirectory, NullLogger<AuthenticateEndpoint>.Instance);

    // the authentication request has no action
    private static StubXtreamHttpClientFactory CreateHttpClientFactory(HttpStatusCode statusCode, string content)
        => new(new Dictionary<string, (HttpStatusCode, string)> { [""] = (statusCode, content) });

    private static JsonObject ReadResponse(XtreamContext context)
    {
        var body = (MemoryStream)context.Response.Body;

        return Assert.IsType<JsonObject>(JsonNode.Parse(body.ToArray()));
    }

    private static XtreamContext CreateContext(string scheme, HostString host)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Scheme = scheme;
        httpContext.Request.Host = host;
        httpContext.Request.QueryString = new QueryString("?username=user&password=secret");
        httpContext.Response.Body = new MemoryStream();
        return new XtreamContext("http", "provider.example.com", 8080, "player_api.php", httpContext, RequestAction.Authenticate, ContentType.Undefined);
    }
}
