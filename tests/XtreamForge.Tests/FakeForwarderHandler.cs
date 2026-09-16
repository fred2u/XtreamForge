using System.Net;
using System.IO.Compression;

namespace XtreamForge.Tests;

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

    public static HttpResponseMessage CreateCompressedJsonResponse(HttpStatusCode statusCode, string json, string contentEncoding = "gzip")
    {
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var buffer = new MemoryStream();

        Stream compressionStream = contentEncoding switch
        {
            "gzip" => new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true),
            "deflate" => new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true),
            "br" => new BrotliStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(contentEncoding), contentEncoding, "Unsupported content encoding for test response.")
        };

        using (compressionStream)
        {
            compressionStream.Write(payloadBytes, 0, payloadBytes.Length);
        }

        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(buffer.ToArray())
        };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        response.Content.Headers.ContentEncoding.Add(contentEncoding);
        return response;
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
