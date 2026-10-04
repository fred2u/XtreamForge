using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.Tests.Xtream;

public class XtreamHttpResponseMessageWriterTests
{
    [Fact]
    public async Task WriteResponseAsync_ForwardsTheContentLengthOfThePartialContent()
    {
        using var responseMessage = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent("12345"u8.ToArray()) };
        responseMessage.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 14, 100);
        var httpContext = CreateHttpContext();

        await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, httpContext.Response, HttpMethods.Get, TestContext.Current.CancellationToken);

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
        var httpContext = CreateHttpContext();

        await XtreamHttpResponseMessageWriter.WriteResponseAsync(responseMessage, httpContext.Response, HttpMethods.Head, TestContext.Current.CancellationToken);

        Assert.Equal(1_000_000, httpContext.Response.ContentLength);
        Assert.Equal(0, httpContext.Response.Body.Length);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }
}
