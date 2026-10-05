using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

public class XtreamRequestForwardEndpointTests
{
    [Fact]
    public async Task ForwardAsync_WhenTheUpstreamBodyBreaksWhileStreaming_AbortsTheConnection()
    {
        var httpContext = new ServerLikeHttpContext();
        httpContext.HttpContext.Request.Method = HttpMethods.Get;
        var endpoint = CreateEndpoint(() => new StreamContent(new BrokenStream("first bytes"u8.ToArray())));

        var result = await endpoint.ForwardAsync(CreateContext(httpContext), TestContext.Current.CancellationToken);

        Assert.IsType<EmptyHttpResult>(result);
        Assert.True(httpContext.IsAborted);
        Assert.Equal(StatusCodes.Status200OK, httpContext.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task ForwardAsync_WhenTheUpstreamFailsBeforeResponding_ReturnsBadGateway()
    {
        var httpContext = new ServerLikeHttpContext();
        httpContext.HttpContext.Request.Method = HttpMethods.Get;
        var endpoint = CreateEndpoint(() => throw new HttpRequestException("connection refused"));

        var result = await endpoint.ForwardAsync(CreateContext(httpContext), TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.False(httpContext.IsAborted);
    }

    private static XtreamRequestForwardEndpoint CreateEndpoint(Func<HttpContent> createContent)
        => new(new ContentHttpClientFactory(createContent), new WatchHistoryQueue(TimeProvider.System), NullLogger<XtreamRequestForwardEndpoint>.Instance);

    private static XtreamContext CreateContext(ServerLikeHttpContext httpContext)
        => new("http", "provider.example.com", 8080, "live/user/secret/1.ts", httpContext.HttpContext, RequestAction.Undefined, ContentType.Undefined);

    // answers 200 with the created content
    private sealed class ContentHttpClientFactory(Func<HttpContent> createContent) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ContentHandler(createContent));

        private sealed class ContentHandler(Func<HttpContent> createContent) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = createContent() });
        }
    }

    // returns the given bytes, then fails as an interrupted upstream connection
    private sealed class BrokenStream(byte[] firstBytes) : Stream
    {
        private bool _hasReturnedFirstBytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_hasReturnedFirstBytes)
                throw new IOException("The upstream connection was interrupted.");

            _hasReturnedFirstBytes = true;
            firstBytes.CopyTo(buffer);
            return firstBytes.Length;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(Read(buffer.AsSpan(offset, count)));

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
