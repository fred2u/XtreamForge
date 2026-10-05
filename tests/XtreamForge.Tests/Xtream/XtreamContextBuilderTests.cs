using Microsoft.AspNetCore.Http;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;

namespace XtreamForge.Tests.Xtream;

public class XtreamContextBuilderTests
{
    private readonly XtreamContextBuilder _builder = new(new XtreamProviderValidator(
        Microsoft.Extensions.Options.Options.Create(new XtreamProxyOptions { AllowedHosts = ["provider.example.com"] })));

    [Fact]
    public void TryBuild_WhenProviderIsInvalid_ReturnsTheValidationError()
    {
        var isBuilt = _builder.TryBuild("http", "not-allowed.example.com", 80, "player_api.php", CreateHttpContext(), out var xtreamContext, out var error);

        Assert.False(isBuilt);
        Assert.Null(xtreamContext);
        Assert.Equal("Upstream host is not allowed.", error);
    }

    [Fact]
    public void TryBuild_NormalizesProtocolHostAndPath()
    {
        var isBuilt = _builder.TryBuild("HTTP", "Provider.Example.com", 8080, "/player_api.php/", CreateHttpContext(), out var xtreamContext, out var error);

        Assert.True(isBuilt);
        Assert.Null(error);
        Assert.NotNull(xtreamContext);
        Assert.Equal("http", xtreamContext.Protocol);
        Assert.Equal("provider.example.com", xtreamContext.Host);
        Assert.Equal(8080, xtreamContext.Port);
        Assert.Equal(new Uri("http://provider.example.com:8080/player_api.php"), xtreamContext.BuildTargetUri());
    }

    [Theory]
    [InlineData("get_vod_categories", RequestAction.GetCategories, ContentType.Vod)]
    [InlineData("get_series_categories", RequestAction.GetCategories, ContentType.Series)]
    [InlineData("get_vod_streams", RequestAction.GetItems, ContentType.Vod)]
    [InlineData("get_series", RequestAction.GetItems, ContentType.Series)]
    [InlineData("get_vod_info", RequestAction.GetInfo, ContentType.Vod)]
    [InlineData("get_series_info", RequestAction.GetInfo, ContentType.Series)]
    [InlineData(" GET_VOD_STREAMS ", RequestAction.GetItems, ContentType.Vod)]
    [InlineData("get_live_streams", RequestAction.Undefined, ContentType.Undefined)]
    [InlineData("", RequestAction.Authenticate, ContentType.Undefined)]
    [InlineData(" ", RequestAction.Authenticate, ContentType.Undefined)]
    public void TryBuild_ClassifiesPlayerApiAction(string action, RequestAction expectedAction, ContentType expectedContentType)
    {
        var httpContext = CreateHttpContext(QueryString.Create("action", action));

        _builder.TryBuild("http", "provider.example.com", 80, "player_api.php", httpContext, out var xtreamContext, out _);

        Assert.NotNull(xtreamContext);
        Assert.Equal(expectedAction, xtreamContext.Action);
        Assert.Equal(expectedContentType, xtreamContext.ContentType);
    }

    [Fact]
    public void TryBuild_WithoutAction_ClassifiesTheAuthentication()
    {
        _builder.TryBuild("http", "provider.example.com", 80, "player_api.php", CreateHttpContext(new QueryString("?username=user&password=secret")), out var xtreamContext, out _);

        Assert.NotNull(xtreamContext);
        Assert.Equal(RequestAction.Authenticate, xtreamContext.Action);
    }

    [Theory]
    [InlineData("xmltv.php")]
    [InlineData("movie/user/pass/1.mp4")]
    public void TryBuild_WhenPathIsNotPlayerApi_ReturnsUndefinedAction(string rest)
    {
        var httpContext = CreateHttpContext(QueryString.Create("action", "get_vod_streams"));

        _builder.TryBuild("http", "provider.example.com", 80, rest, httpContext, out var xtreamContext, out _);

        Assert.NotNull(xtreamContext);
        Assert.Equal(RequestAction.Undefined, xtreamContext.Action);
        Assert.Equal(ContentType.Undefined, xtreamContext.ContentType);
    }

    private static DefaultHttpContext CreateHttpContext(QueryString queryString = default)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = queryString;
        return httpContext;
    }
}
