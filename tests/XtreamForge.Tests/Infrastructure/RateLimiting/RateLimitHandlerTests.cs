using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ApiService.Infrastructure.RateLimiting;
using XtreamForge.ApiService.Options;

namespace XtreamForge.Tests.Infrastructure.RateLimiting;

public sealed class RateLimitHandlerTests : IDisposable
{
    private static readonly Uri ProviderUri = new("http://provider.example.com:8080/player_api.php?username=user&password=secret");
    private const string ProviderHost = "http://provider.example.com:8080";

    private readonly SteppingTimeProvider _time = new();
    private readonly TestMeterFactory _meterFactory = new();
    private readonly UpstreamRateLimiter _limiter;

    public RateLimitHandlerTests()
    {
        _limiter = new UpstreamRateLimiter(_time, NullLogger<UpstreamRateLimiter>.Instance, _meterFactory);
    }

    [Fact]
    public async Task SendAsync_WhenNotRateLimited_ReturnsTheResponseWithoutDelay()
    {
        var upstream = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = CreateInvoker(upstream);

        using var response = await SendRequestAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, upstream.CallCount);
        Assert.Empty(_time.Delays);
    }

    [Fact]
    public async Task SendAsync_WhenRateLimited_WaitsAndSendsTheRequestAgain()
    {
        var upstream = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = CreateInvoker(upstream);

        using var response = await SendRequestAsync(invoker, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, upstream.CallCount);
        Assert.Equal([TimeSpan.FromSeconds(1)], _time.Delays);
    }

    [Fact]
    public async Task SendAsync_WhenRetryAfterIsSent_WaitsForIt()
    {
        var upstream = new SequenceHandler(
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return response;
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        using var invoker = CreateInvoker(upstream);

        using var response = await SendRequestAsync(invoker, HttpMethod.Get);

        Assert.Equal([TimeSpan.FromSeconds(7)], _time.Delays);
    }

    [Fact]
    public async Task SendAsync_WhenAlwaysRateLimited_ReturnsTheLastResponseAfterMaximumAttempts()
    {
        var upstream = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var invoker = CreateInvoker(upstream);

        using var response = await SendRequestAsync(invoker, HttpMethod.Head);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(RateLimitHandler.MaximumAttempts, upstream.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(4), _limiter.GetInterval(ProviderHost));
    }

    [Fact]
    public async Task SendAsync_WhenRequestHasABody_DoesNotSendItAgain()
    {
        var upstream = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var invoker = CreateInvoker(upstream);

        using var response = await SendRequestAsync(invoker, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, upstream.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(1), _limiter.GetInterval(ProviderHost));
    }

    [Fact]
    public async Task UpstreamHttpClients_HandleRateLimitsBeforeTheResilienceRetries()
    {
        foreach (var clientName in new[] { XtreamProxyOptions.HttpClientName, TmdbOptions.HttpClientName })
        {
            var upstream = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            var services = new ServiceCollection();
            services.AddLogging();
            services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
            services.AddSingleton(Options.Create(new TmdbOptions { BaseUrl = "https://tmdb.example.com/3/" }));
            services.AddHttpClients();
            // Only the rate limiter waits are skipped: the resilience pipeline keeps the system clock,
            // otherwise its timeouts would fire immediately and cancel the request.
            services.AddSingleton(new UpstreamRateLimiter(new SteppingTimeProvider(), NullLogger<UpstreamRateLimiter>.Instance, _meterFactory));
            services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => upstream);
            await using var provider = services.BuildServiceProvider();

            using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            using var response = await client.GetAsync(ProviderUri, TestContext.Current.CancellationToken);

            // Only RateLimitHandler retries: the standard resilience pipeline would multiply the attempts.
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(RateLimitHandler.MaximumAttempts, upstream.CallCount);
        }
    }

    public void Dispose()
    {
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private HttpMessageInvoker CreateInvoker(HttpMessageHandler upstream) =>
        new(new RateLimitHandler(_limiter, _time) { InnerHandler = upstream });

    private static async Task<HttpResponseMessage> SendRequestAsync(HttpMessageInvoker invoker, HttpMethod method)
    {
        using var request = new HttpRequestMessage(method, ProviderUri);
        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Answers with the given responses in order; the last one is repeated.</summary>
    private sealed class SequenceHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = responses[Math.Min(CallCount, responses.Length - 1)](request);
            CallCount++;

            return Task.FromResult(response);
        }
    }
}
