using System.Net;
using System.Web;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.Tests.Infrastructure;

/// <summary>
/// Returns a canned response per Xtream <c>action</c> query parameter and records the requested URIs, and their redacted form.
/// </summary>
public sealed class StubXtreamHttpClientFactory(IReadOnlyDictionary<string, (HttpStatusCode StatusCode, string Content)> responses) : IHttpClientFactory
{
    public List<Uri> RequestedUris { get; } = [];

    /// <summary>The requested URIs as written by the telemetry and the logs of the Xtream HTTP client.</summary>
    public List<Uri?> RedactedRequestUris { get; } = [];

    private IReadOnlyDictionary<string, (HttpStatusCode StatusCode, string Content)> Responses { get; } = responses;

    public HttpClient CreateClient(string name) => new(new StubHandler(this));

    private sealed class StubHandler(StubXtreamHttpClientFactory factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            factory.RequestedUris.Add(uri);
            factory.RedactedRequestUris.Add(XtreamCredentialRedaction.RedactRequestUri(request));

            var action = HttpUtility.ParseQueryString(uri.Query)["action"] ?? string.Empty;
            var (statusCode, content) = factory.Responses.TryGetValue(action, out var response)
                ? response
                : (HttpStatusCode.NotFound, string.Empty);

            return Task.FromResult(new HttpResponseMessage(statusCode) { Content = new StringContent(content) });
        }
    }
}
