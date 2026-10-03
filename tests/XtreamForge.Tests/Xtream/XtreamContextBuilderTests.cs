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
    public void Build_WhenProviderIsInvalid_ReturnsInvalidResult()
    {
        var result = _builder.Build("http", "not-allowed.example.com", 80, "player_api.php", CreateHttpContext());

        Assert.False(result.IsValid);
        Assert.Null(result.XtreamContext);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Build_NormalizesProtocolHostAndPath()
    {
        var result = _builder.Build("HTTP", "Provider.Example.com", 8080, "/player_api.php/", CreateHttpContext());

        Assert.True(result.IsValid);
        Assert.NotNull(result.XtreamContext);
        Assert.Equal("http", result.XtreamContext.Protocol);
        Assert.Equal("provider.example.com", result.XtreamContext.Host);
        Assert.Equal(8080, result.XtreamContext.Port);
        Assert.Equal(new Uri("http://provider.example.com:8080/player_api.php"), result.XtreamContext.BuildTargetUri());
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
    public void Build_ClassifiesPlayerApiAction(string action, RequestAction expectedAction, ContentType expectedContentType)
    {
        var httpContext = CreateHttpContext(QueryString.Create("action", action));

        var result = _builder.Build("http", "provider.example.com", 80, "player_api.php", httpContext);

        Assert.NotNull(result.XtreamContext);
        Assert.Equal(expectedAction, result.XtreamContext.Action);
        Assert.Equal(expectedContentType, result.XtreamContext.ContentType);
    }

    [Fact]
    public void Build_WithoutAction_ClassifiesTheAuthentication()
    {
        var result = _builder.Build("http", "provider.example.com", 80, "player_api.php", CreateHttpContext(new QueryString("?username=user&password=secret")));

        Assert.NotNull(result.XtreamContext);
        Assert.Equal(RequestAction.Authenticate, result.XtreamContext.Action);
    }

    [Theory]
    [InlineData("xmltv.php")]
    [InlineData("movie/user/pass/1.mp4")]
    public void Build_WhenPathIsNotPlayerApi_ReturnsUndefinedAction(string rest)
    {
        var httpContext = CreateHttpContext(QueryString.Create("action", "get_vod_streams"));

        var result = _builder.Build("http", "provider.example.com", 80, rest, httpContext);

        Assert.NotNull(result.XtreamContext);
        Assert.Equal(RequestAction.Undefined, result.XtreamContext.Action);
        Assert.Equal(ContentType.Undefined, result.XtreamContext.ContentType);
    }

    private static DefaultHttpContext CreateHttpContext(QueryString queryString = default)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.QueryString = queryString;
        return httpContext;
    }
}
