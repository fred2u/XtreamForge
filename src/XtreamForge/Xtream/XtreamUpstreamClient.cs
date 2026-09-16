using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace XtreamForge.Xtream;

public sealed class XtreamUpstreamClient(HttpClient httpClient)
{
    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken = default) =>
        httpClient.SendAsync(request, completionOption, cancellationToken);

    public async Task<T?> ReadFromJsonAsync<T>(HttpContent content, CancellationToken cancellationToken = default)
    {
        await using var payloadStream = await OpenJsonStreamAsync(content, cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(payloadStream, cancellationToken: cancellationToken);
    }

    public async IAsyncEnumerable<JsonElement> ReadJsonArrayAsync(
        HttpContent content,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var payloadStream = await OpenJsonStreamAsync(content, cancellationToken);
        await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
                           payloadStream,
                           topLevelValues: false,
                           cancellationToken: cancellationToken))
        {
            yield return item;
        }
    }

    private static async Task<Stream> OpenJsonStreamAsync(HttpContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        Stream payloadStream = await content.ReadAsStreamAsync(cancellationToken);
        foreach (var contentEncoding in content.Headers.ContentEncoding.Reverse())
        {
            payloadStream = CreateDecompressionStream(payloadStream, contentEncoding);
        }

        return payloadStream;
    }

    private static Stream CreateDecompressionStream(Stream payloadStream, string? contentEncoding) =>
        contentEncoding?.Trim().ToLowerInvariant() switch
        {
            null or "" or "identity" => payloadStream,
            "gzip" or "x-gzip" => new GZipStream(payloadStream, CompressionMode.Decompress),
            "deflate" => new DeflateStream(payloadStream, CompressionMode.Decompress),
            "br" => new BrotliStream(payloadStream, CompressionMode.Decompress),
            _ => throw new HttpRequestException($"Unsupported upstream content encoding '{contentEncoding}'.")
        };
}
