using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ApiService.Infrastructure.RateLimiting;
using XtreamForge.ApiService.Options;

namespace XtreamForge.Tests.Infrastructure;

public sealed class XtreamHttpClientLoggerTests : IDisposable
{
    private readonly TestMeterFactory _meterFactory = new();

    [Theory]
    [InlineData("http://provider.example.com:8080/movie/user/secret/42.mp4", "http://provider.example.com:8080/movie/***/***/42.mp4")]
    [InlineData("http://provider.example.com:8080/player_api.php?username=user&password=secret", "http://provider.example.com:8080/player_api.php?username=***&password=***")]
    public async Task XtreamHttpClient_LogsTheRequestsWithoutCredentials(string requestUri, string redactedUri)
    {
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(loggerProvider));
        services.AddHttpClients();
        services.AddSingleton(new UpstreamRateLimiter(new SteppingTimeProvider(), NullLogger<UpstreamRateLimiter>.Instance, _meterFactory));
        services.AddHttpClient(XtreamProxyOptions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new OkHandler());
        await using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(XtreamProxyOptions.HttpClientName);
        using var response = await client.GetAsync(requestUri, TestContext.Current.CancellationToken);

        Assert.Contains(loggerProvider.Messages, message => message.Contains(redactedUri, StringComparison.Ordinal));
        Assert.DoesNotContain(loggerProvider.Messages, message => message.Contains("secret", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    /// <summary>Records the formatted message of every log entry, whatever its category.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
            // nothing to release: the messages stay readable after the service provider is disposed
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => messages.Enqueue(formatter(state, exception));
        }
    }
}
