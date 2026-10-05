using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Xtream;

public class XtreamHttpForwarderTests
{
    private static readonly Uri TargetUri = new("http://provider.example.com:8080/player_api.php");

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void CreateRequestMessage_KeepsMethodAndTargetWithoutContent(string method)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Headers.ContentType = "application/json";
        httpContext.Request.ContentLength = 2;
        httpContext.Request.Body = new MemoryStream("{}"u8.ToArray());

        using var requestMessage = XtreamHttpForwarder.CreateRequestMessage(TargetUri, httpContext.Request);

        Assert.Equal(method, requestMessage.Method.Method);
        Assert.Equal(TargetUri, requestMessage.RequestUri);
        Assert.Null(requestMessage.Content);
    }

    [Fact]
    public void CreateRequestMessage_ForwardsEndToEndHeadersAndSkipsHostAndHopByHopHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers.Host = "xtreamforge.example.com";
        httpContext.Request.Headers.UserAgent = "IPTV Player";
        httpContext.Request.Headers.Connection = "keep-alive, X-Custom-Hop";
        httpContext.Request.Headers["X-Custom-Hop"] = "value";
        httpContext.Request.Headers.KeepAlive = "timeout=5";

        using var requestMessage = XtreamHttpForwarder.CreateRequestMessage(TargetUri, httpContext.Request);

        Assert.Equal("IPTV Player", requestMessage.Headers.UserAgent.ToString());
        Assert.False(requestMessage.Headers.Contains("Host"));
        Assert.False(requestMessage.Headers.Contains("Connection"));
        Assert.False(requestMessage.Headers.Contains("Keep-Alive"));
        Assert.False(requestMessage.Headers.Contains("X-Custom-Hop"));
    }

    [Fact]
    public async Task WriteResponseAsync_ForwardsTheContentLengthOfThePartialContent()
    {
        using var responseMessage = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("12345"u8.ToArray()) };
        responseMessage.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 14, 100);
        var httpContext = CreateHttpContext(HttpMethods.Get);

        await XtreamHttpForwarder.WriteResponseAsync(responseMessage, httpContext.Response, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCodes.Status206PartialContent, httpContext.Response.StatusCode);
        Assert.Equal(5, httpContext.Response.ContentLength);
        Assert.Equal("bytes 10-14/100", httpContext.Response.Headers.ContentRange.ToString());
        Assert.Equal("12345"u8.ToArray(), ((MemoryStream)httpContext.Response.Body).ToArray());
    }

    [Fact]
    public async Task WriteResponseAsync_ForAHeadRequest_ForwardsTheContentLengthWithoutBody()
    {
        using var responseMessage = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        responseMessage.Content.Headers.ContentLength = 1_000_000;
        var httpContext = CreateHttpContext(HttpMethods.Head);

        await XtreamHttpForwarder.WriteResponseAsync(responseMessage, httpContext.Response, TestContext.Current.CancellationToken);

        Assert.Equal(1_000_000, httpContext.Response.ContentLength);
        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    [Fact]
    public async Task WriteResponseAsync_SkipsHopByHopHeaders()
    {
        using var responseMessage = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        responseMessage.Headers.Connection.Add("X-Custom-Hop");
        responseMessage.Headers.TryAddWithoutValidation("X-Custom-Hop", "value");
        responseMessage.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");
        responseMessage.Headers.TryAddWithoutValidation("X-End-To-End", "value");
        var httpContext = CreateHttpContext(HttpMethods.Get);

        await XtreamHttpForwarder.WriteResponseAsync(responseMessage, httpContext.Response, TestContext.Current.CancellationToken);

        Assert.Equal("value", httpContext.Response.Headers["X-End-To-End"].ToString());
        Assert.False(httpContext.Response.Headers.ContainsKey("Connection"));
        Assert.False(httpContext.Response.Headers.ContainsKey("Keep-Alive"));
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Custom-Hop"));
    }

    [Theory]
    [InlineData("movie/user/secret/42.mkv", true)]
    [InlineData("user/secret/7.ts", true)]
    [InlineData("player_api.php", false)]
    public async Task SendAsync_MarksOnlyTheMediaStreamRequests(string path, bool isStream)
    {
        var httpClientFactory = new RecordingHttpClientFactory();
        var httpContext = CreateHttpContext(HttpMethods.Get);
        var xtreamContext = new XtreamContext("http", "provider.example.com", 8080, path, httpContext, RequestAction.Undefined, ContentType.Undefined);

        using var responseMessage = await XtreamHttpForwarder.SendAsync(httpClientFactory, xtreamContext, xtreamContext.BuildTargetUri(), TestContext.Current.CancellationToken);

        Assert.Equal(isStream, httpClientFactory.IsStreamRequest);
    }

    private static DefaultHttpContext CreateHttpContext(string method)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    // answers 200 and records whether the sent request was marked as a stream request
    private sealed class RecordingHttpClientFactory : HttpMessageHandler, IHttpClientFactory
    {
        public bool? IsStreamRequest { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            IsStreamRequest = StreamRequest.IsMarked(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
