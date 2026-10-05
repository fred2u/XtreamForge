using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using XtreamForge.ApiService.Infrastructure;

namespace XtreamForge.Tests.Infrastructure;

public sealed class ForwardedHeadersExtensionsTests : IAsyncDisposable
{
    private WebApplication? _app;

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.18.3.4")]
    [InlineData("127.0.0.1")]
    public async Task UseForwardedHeaders_FromATrustedProxy_UsesTheOriginalSchemeAndHost(string remoteAddress)
    {
        var client = await StartAsync(remoteAddress);

        var origin = await GetOriginAsync(client);

        Assert.Equal("https://iptv.example.com", origin);
    }

    [Fact]
    public async Task UseForwardedHeaders_FromAnUnknownSender_IgnoresTheHeaders()
    {
        var client = await StartAsync("203.0.113.9");

        var origin = await GetOriginAsync(client);

        Assert.Equal("http://localhost", origin);
    }

    [Fact]
    public async Task AddForwardedHeaders_WithAnInvalidNetwork_FailsAtStartup()
    {
        var exception = await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(
            () => StartAsync("10.0.0.5", knownNetwork: "not-a-network"));

        Assert.Contains("ReverseProxy:KnownNetworks", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<string> GetOriginAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/origin");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "iptv.example.com");
        request.Headers.Add("X-Forwarded-For", "198.51.100.7");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private async Task<HttpClient> StartAsync(string remoteAddress, string knownNetwork = "172.18.0.0/16")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.5",
            ["ReverseProxy:KnownNetworks:0"] = knownNetwork
        });

        builder.Services.AddForwardedHeaders();

        _app = builder.Build();

        // the test server has no connection: the address of the sender is set before the forwarded headers are read
        _app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            return next(context);
        });
        _app.UseForwardedHeaders();
        _app.MapGet("/origin", (HttpRequest request) => $"{request.Scheme}://{request.Host}");

        await _app.StartAsync(TestContext.Current.CancellationToken);

        return _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
