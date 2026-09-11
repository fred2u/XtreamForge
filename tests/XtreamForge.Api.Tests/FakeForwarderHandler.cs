using System.Net;

namespace XtreamForge.Api.Tests;

internal sealed class FakeForwarderHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = [];

    public IReadOnlyList<CapturedRequest> Requests => _requests;

    public TaskCompletionSource<CapturedRequest> RequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public CancellationToken LastCancellationToken { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastCancellationToken = cancellationToken;

        var capturedRequest = await CapturedRequest.CreateAsync(request, cancellationToken);
        _requests.Add(capturedRequest);
        RequestReceived.TrySetResult(capturedRequest);

        return await responder(request, cancellationToken);
    }

    public static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json)
        };
    }
}

internal sealed record CapturedRequest(
    string Method,
    Uri? RequestUri,
    string? Host,
    IReadOnlyDictionary<string, string[]> Headers,
    string? Body)
{
    public static async Task<CapturedRequest> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers)
        {
            headers[header.Key] = [.. header.Value];
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = [.. header.Value];
            }
        }

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new CapturedRequest(
            request.Method.Method,
            request.RequestUri,
            request.Headers.Host,
            headers,
            body);
    }
}
