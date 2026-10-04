using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Xtream;

public class XtreamUpstreamFailureTests
{
    public static TheoryData<Exception> UpstreamFailures => new()
    {
        new HttpRequestException("connection refused"),
        new JsonException("invalid JSON"),
        new IOException("body interrupted"),
        new TaskCanceledException("timeout")
    };

    [Theory]
    [MemberData(nameof(UpstreamFailures))]
    public void IsUpstreamFailure_RecognizesTheUpstreamFailures(Exception exception)
    {
        Assert.True(XtreamUpstreamFailure.IsUpstreamFailure(exception, CancellationToken.None));
    }

    [Fact]
    public void IsUpstreamFailure_WhenTheClientCancelledTheRequest_ReturnsFalse()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.False(XtreamUpstreamFailure.IsUpstreamFailure(new OperationCanceledException(cancellation.Token), cancellation.Token));
    }

    [Fact]
    public void IsUpstreamFailure_ForAnotherException_ReturnsFalse()
    {
        Assert.False(XtreamUpstreamFailure.IsUpstreamFailure(new InvalidOperationException(), CancellationToken.None));
    }

    [Theory]
    [InlineData(false, StatusCodes.Status502BadGateway)]
    [InlineData(true, StatusCodes.Status504GatewayTimeout)]
    public void Handle_BeforeTheResponseHasStarted_ReturnsTheGatewayStatus(bool isTimeout, int expectedStatusCode)
    {
        var httpContext = new ServerLikeHttpContext();
        Exception exception = isTimeout ? new TaskCanceledException("timeout") : new HttpRequestException("connection refused");

        var result = XtreamUpstreamFailure.Handle(exception, CreateContext(httpContext), NullLogger.Instance);

        Assert.Equal(expectedStatusCode, Assert.IsType<StatusCodeHttpResult>(result).StatusCode);
        Assert.False(httpContext.IsAborted);
    }

    [Fact]
    public async Task Handle_AfterTheResponseHasStarted_AbortsTheConnectionWithoutChangingTheStatus()
    {
        var httpContext = new ServerLikeHttpContext();
        await httpContext.HttpContext.Response.StartAsync(TestContext.Current.CancellationToken);

        var result = XtreamUpstreamFailure.Handle(new IOException("body interrupted"), CreateContext(httpContext), NullLogger.Instance);

        Assert.IsType<EmptyHttpResult>(result);
        Assert.True(httpContext.IsAborted);
        Assert.Equal(StatusCodes.Status200OK, httpContext.HttpContext.Response.StatusCode);
    }

    private static XtreamContext CreateContext(ServerLikeHttpContext httpContext)
        => new("http", "provider.example.com", 8080, "movie/user/secret/1.mp4", httpContext.HttpContext, RequestAction.Undefined, ContentType.Undefined);
}
