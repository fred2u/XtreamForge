using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using System.Net;
using System.Net.Http.Headers;
using XtreamForge.ApiService.Infrastructure.RateLimiting;
using XtreamForge.ApiService.Options;

namespace XtreamForge.ApiService.Infrastructure;

public static class HttpClientsExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHttpClients()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<UpstreamRateLimiter>();
            services.AddTransient<RateLimitHandler>();

            // HTTP 429 is handled by RateLimitHandler (per-host adaptive spacing, Retry-After): the standard resilience
            // pipeline of the service defaults must neither retry it immediately nor count it for its circuit breaker.
            services.ConfigureAll<HttpStandardResilienceOptions>(resilience =>
            {
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(IsTransientButNotRateLimited(args.Outcome));
                resilience.CircuitBreaker.ShouldHandle = args => ValueTask.FromResult(IsTransientButNotRateLimited(args.Outcome));
            });

            services
                .AddHttpClient(XtreamProxyOptions.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    AutomaticDecompression = DecompressionMethods.All
                })
                .AddOutermostRateLimitHandler();

            services
                .AddHttpClient(TmdbOptions.HttpClientName, (serviceProvider, client) =>
                {
                    var tmdbOptions = serviceProvider.GetRequiredService<IOptions<TmdbOptions>>().Value;
                    var readAccessToken = tmdbOptions.ApiKey;

                    client.BaseAddress = new Uri(tmdbOptions.BaseUrl);

                    if (!string.IsNullOrWhiteSpace(readAccessToken))
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readAccessToken);
                })
                .AddOutermostRateLimitHandler();

            return services;
        }
    }

    // Inserted before the handlers of the service defaults (resilience): its waits are not counted by the resilience
    // timeouts, and it sees the final response of each attempt.
    private static void AddOutermostRateLimitHandler(this IHttpClientBuilder builder) =>
        builder.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
            handlers.Insert(0, serviceProvider.GetRequiredService<RateLimitHandler>()));

    private static bool IsTransientButNotRateLimited(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests && HttpClientResiliencePredicates.IsTransient(outcome);
}
