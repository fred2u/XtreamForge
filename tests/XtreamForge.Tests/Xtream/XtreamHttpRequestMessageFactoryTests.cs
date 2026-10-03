using Microsoft.AspNetCore.Http;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.Tests.Xtream;

public class XtreamHttpRequestMessageFactoryTests
{
    private static readonly Uri TargetUri = new("http://provider.example.com:8080/player_api.php");

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public void Create_KeepsMethodAndTargetWithoutContent(string method)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Headers.ContentType = "application/json";
        httpContext.Request.ContentLength = 2;
        httpContext.Request.Body = new MemoryStream("{}"u8.ToArray());

        using var requestMessage = XtreamHttpRequestMessageFactory.Create(TargetUri, httpContext.Request);

        Assert.Equal(method, requestMessage.Method.Method);
        Assert.Equal(TargetUri, requestMessage.RequestUri);
        Assert.Null(requestMessage.Content);
    }

    [Fact]
    public void Create_ForwardsEndToEndHeadersAndSkipsHostAndHopByHopHeaders()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers.Host = "xtreamforge.example.com";
        httpContext.Request.Headers.UserAgent = "IPTV Player";
        httpContext.Request.Headers.Connection = "keep-alive, X-Custom-Hop";
        httpContext.Request.Headers["X-Custom-Hop"] = "value";
        httpContext.Request.Headers.KeepAlive = "timeout=5";

        using var requestMessage = XtreamHttpRequestMessageFactory.Create(TargetUri, httpContext.Request);

        Assert.Equal("IPTV Player", requestMessage.Headers.UserAgent.ToString());
        Assert.False(requestMessage.Headers.Contains("Host"));
        Assert.False(requestMessage.Headers.Contains("Connection"));
        Assert.False(requestMessage.Headers.Contains("Keep-Alive"));
        Assert.False(requestMessage.Headers.Contains("X-Custom-Hop"));
    }
}
