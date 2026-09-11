using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
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
        var client = _factory.CreateClient();

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
        var client = _factory.CreateClient();

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

    [Fact]
    public async Task InvalidProtocol_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/ftp/example.com/80/player_api.php");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SupportedXtreamAction_IsInterceptedLocally()
    {
        var handler = new FakeForwarderHandler((_, _) =>
            Task.FromResult(FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}")));

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("X-XtreamForge-Intercepted")));
        Assert.Contains("get_vod_categories", content);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UnsupportedXtreamAction_IsForwarded_WithQueryStringPreserved()
    {
        var handler = new FakeForwarderHandler((request, _) =>
        {
            Assert.NotNull(request.RequestUri);
            Assert.Equal("http", request.RequestUri.Scheme);
            Assert.Equal("example.com", request.RequestUri.Host);
            Assert.Equal(8080, request.RequestUri.Port);
            Assert.Equal("/player_api.php", request.RequestUri.AbsolutePath);
            Assert.Equal("?action=get_live_categories&category_id=12", request.RequestUri.Query);
            return Task.FromResult(FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}"));
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/http/example.com/8080/player_api.php?action=get_live_categories&category_id=12");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task PlayerApiWithoutAction_IsForwarded()
    {
        var handler = new FakeForwarderHandler((request, _) =>
        {
            Assert.NotNull(request.RequestUri);
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.Equal("example.com", request.RequestUri.Host);
            Assert.Equal(443, request.RequestUri.Port);
            Assert.Equal("/player_api.php", request.RequestUri.AbsolutePath);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            return Task.FromResult(FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}"));
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Forwarding_PreservesMethodAndHeaders_ButNotHost()
    {
        var handler = new FakeForwarderHandler((_, _) =>
            Task.FromResult(FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.Accepted, "{\"forwarded\":true}")));

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/https/example.com/443/player_api.php?action=get_live_categories")
        {
            Content = new StringContent("sample-body")
        };
        request.Headers.Host = "client.example.test";
        request.Headers.Add("X-Test-Header", "header-value");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var response = await client.SendAsync(request);
        var forwardedRequest = Assert.Single(handler.Requests);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("POST", forwardedRequest.Method);
        Assert.Equal("sample-body", forwardedRequest.Body);
        Assert.True(forwardedRequest.Headers.TryGetValue("X-Test-Header", out var headerValues));
        Assert.Equal(["header-value"], headerValues);
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
            response.Headers.Connection.Add("keep-alive");
            response.Headers.TransferEncodingChunked = true;
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
        Assert.False(response.Headers.TransferEncodingChunked.HasValue && response.Headers.TransferEncodingChunked.Value);
        Assert.Equal("upstream-body", content);
    }

    [Fact]
    public async Task HeadRequests_AreForwardedWithoutWritingResponseBody()
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
