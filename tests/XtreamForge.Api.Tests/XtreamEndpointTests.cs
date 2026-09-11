using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Api.Tests;

public sealed class XtreamEndpointTests : IClassFixture<XtreamForgeApiFactory>
{
    private readonly XtreamForgeApiFactory _factory;

    public XtreamEndpointTests(XtreamForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStatus_ReturnsExpectedPayload()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/status");
        var payload = await response.Content.ReadFromJsonAsync<StatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("XtreamForge", payload.ApplicationName);
        Assert.Equal("Healthy", payload.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.ApplicationVersion));
    }

    [Fact]
    public async Task GetDashboard_ReturnsIntegratedRazorPagesUi()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("XtreamForge", content);
        Assert.Contains("Dashboard", content);
        Assert.Contains("Categories", content);
        Assert.Contains("Settings", content);
    }

    [Fact]
    public void DependencyInjection_CanConstructInfrastructureServices()
    {
        using var scope = _factory.Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();

        Assert.NotNull(dbContext);
    }

    [Theory]
    [InlineData("http", "/http/example.com/8080/player_api.php?action=get_live_categories")]
    [InlineData("https", "/https/example.com/443/player_api.php?action=get_live_categories")]
    public async Task SupportedProtocols_AreAccepted(string expectedScheme, string requestPath)
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedScheme, Assert.Single(handler.Requests).RequestUri?.Scheme);
    }

    [Fact]
    public async Task InvalidProtocol_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/ftp/example.com/80/player_api.php");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("65536")]
    public async Task InvalidPort_ReturnsBadRequest(string port)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/http/example.com/{port}/player_api.php");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("get_vod_categories", null, null)]
    [InlineData("get_series_categories", null, null)]
    [InlineData("get_vod_streams", "category_id", "101")]
    [InlineData("get_series", "category_id", "202")]
    [InlineData("get_vod_info", "vod_id", "303")]
    [InlineData("get_series_info", "series_id", "404")]
    public async Task RecognizedPlayerApiActions_AreForwardedUnchanged(
        string action,
        string? extraKey,
        string? extraValue)
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler, failIfDatabaseAccessed: true);
        using var client = factory.CreateClient();

        var queryParameters = new List<string>
        {
            "username=test-user",
            "******",
            $"action={action}"
        };

        if (extraKey is not null && extraValue is not null)
        {
            queryParameters.Add($"{extraKey}={extraValue}");
        }

        var response = await client.GetAsync($"/https/example.com/443/PLAYER_API.PHP?{string.Join('&', queryParameters)}");

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(forwardedRequest.RequestUri);
        Assert.Equal("https", forwardedRequest.RequestUri.Scheme);
        Assert.Equal("example.com", forwardedRequest.RequestUri.Host);
        Assert.Equal(443, forwardedRequest.RequestUri.Port);
        Assert.Equal("/PLAYER_API.PHP", forwardedRequest.RequestUri.AbsolutePath);
        Assert.Contains($"action={action}", forwardedRequest.RequestUri.Query, StringComparison.Ordinal);

        if (extraKey is not null && extraValue is not null)
        {
            Assert.Contains($"{extraKey}={extraValue}", forwardedRequest.RequestUri.Query, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnknownPlayerApiAction_IsForwarded()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/http/example.com/8080/player_api.php?action=get_live_categories&category_id=12");

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?action=get_live_categories&category_id=12", forwardedRequest.RequestUri?.Query);
    }

    [Fact]
    public async Task PlayerApiWithoutAction_IsForwarded()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?username=user&******");

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/player_api.php", forwardedRequest.RequestUri?.AbsolutePath);
        Assert.Equal("?username=user&******", forwardedRequest.RequestUri?.Query);
    }

    [Theory]
    [InlineData("/https/example.com/443?ping=true", "/", "?ping=true")]
    [InlineData("/http/example.com/8080", "/", "")]
    [InlineData("/http/example.com/8080/xmltv.php?username=user&******", "/xmltv.php", "?username=user&******")]
    public async Task NonPlayerApiRequests_AreForwarded(string requestPath, string expectedPath, string expectedQuery)
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(requestPath);

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedPath, forwardedRequest.RequestUri?.AbsolutePath);
        Assert.Equal(expectedQuery, forwardedRequest.RequestUri?.Query);
    }

    [Fact]
    public async Task Forwarding_PreservesMethodHeadersAndRequestBody_ButNotHostOrHopByHopHeaders()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/https/example.com/443/player_api.php?action=get_live_categories")
        {
            Content = new StringContent("sample-body")
        };
        request.Headers.Host = "client.example.test";
        request.Headers.Add("X-Test-Header", "header-value");
        request.Headers.Connection.Add("X-Transient");
        request.Headers.Add("X-Transient", "remove-me");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var response = await client.SendAsync(request);
        var forwardedRequest = Assert.Single(handler.Requests);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("POST", forwardedRequest.Method);
        Assert.Equal("sample-body", forwardedRequest.Body);
        Assert.True(forwardedRequest.Headers.TryGetValue("X-Test-Header", out var headerValues));
        Assert.Equal(["header-value"], headerValues);
        Assert.False(forwardedRequest.Headers.ContainsKey("Connection"));
        Assert.False(forwardedRequest.Headers.ContainsKey("X-Transient"));
        Assert.Null(forwardedRequest.Host);
    }

    [Fact]
    public async Task Forwarding_PropagatesStatusHeadersAndBody_AndRemovesHopByHopHeaders()
    {
        var handler = new FakeForwarderHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("upstream-body")
            };
            response.Headers.Add("X-Upstream-Header", "propagated");
            response.Headers.Connection.Add("X-Hop-Response");
            response.Headers.TransferEncodingChunked = true;
            response.Headers.Add("X-Hop-Response", "remove-me");
            response.Content.Headers.Add("X-Upstream-Content-Header", "content-value");
            return Task.FromResult(response);
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_live_categories");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("propagated", Assert.Single(response.Headers.GetValues("X-Upstream-Header")));
        Assert.Equal("content-value", Assert.Single(GetHeaderValues(response, "X-Upstream-Content-Header")));
        Assert.False(response.Headers.Contains("Connection"));
        Assert.False(response.Headers.Contains("X-Hop-Response"));
        Assert.False(response.Headers.TransferEncodingChunked.HasValue && response.Headers.TransferEncodingChunked.Value);
        Assert.Equal("upstream-body", content);
    }

    [Fact]
    public async Task HeadRequests_DoNotCopyAResponseBody()
    {
        var handler = new FakeForwarderHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent("ignored-body")
            });
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/https/example.com/443/player_api.php?action=get_live_categories");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(body);
    }

    [Fact]
    public async Task RequestCancellation_IsPropagatedThroughRequestAborted()
    {
        var handler = new FakeForwarderHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}");
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        using var cancellationTokenSource = new CancellationTokenSource();

        var requestTask = client.GetAsync(
            "/https/example.com/443/player_api.php?action=get_live_categories",
            cancellationTokenSource.Token);

        await handler.RequestReceived.Task;
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        Assert.True(handler.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task UpstreamConnectionFailure_ReturnsBadGateway_WithoutLoggingCredentials()
    {
        var logSink = new TestLogSink();
        var handler = new FakeForwarderHandler((_, _) => throw new HttpRequestException("Connection refused"));

        using var factory = _factory.WithForwarderHandler(handler, logSink);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?username=test-user&******");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains(logSink.Messages, message => message.Contains("example.com:443", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("test-password", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("username=test-user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpstreamTimeout_ReturnsGatewayTimeout_WithoutLoggingCredentials()
    {
        var logSink = new TestLogSink();
        var handler = new FakeForwarderHandler((_, _) =>
            throw new TaskCanceledException("Timed out", new TimeoutException("upstream timeout")));

        using var factory = _factory.WithForwarderHandler(handler, logSink);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/http/example.com/8080/player_api.php?username=user&******");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains(logSink.Messages, message => message.Contains("example.com:8080", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("very-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("series_id=42", StringComparison.Ordinal));
    }

    private static FakeForwarderHandler CreateForwardingHandler() =>
        new((_, _) => Task.FromResult(FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}")));

    private static IEnumerable<string> GetHeaderValues(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var responseHeaderValues))
        {
            return responseHeaderValues;
        }

        if (response.Content.Headers.TryGetValues(headerName, out var contentHeaderValues))
        {
            return contentHeaderValues;
        }

        throw new InvalidOperationException($"The given header was not found: {headerName}.");
    }

    private sealed record StatusResponse(string ApplicationName, string ApplicationVersion, string Status);
}
