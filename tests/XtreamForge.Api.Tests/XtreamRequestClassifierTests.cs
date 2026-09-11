using Microsoft.AspNetCore.Http;
using XtreamForge.Api.Services;
using XtreamForge.Infrastructure.Models;

namespace XtreamForge.Api.Tests;

public sealed class XtreamRequestClassifierTests
{
    private readonly XtreamRequestClassifier _classifier = new();

    [Theory]
    [InlineData("player_api.php", "get_vod_categories", "get_vod_categories", true, ContentType.Vod, true)]
    [InlineData("player_api.php", "get_series_categories", "get_series_categories", true, ContentType.Series, true)]
    [InlineData("player_api.php", "get_vod_streams", "get_vod_streams", true, ContentType.Vod, false)]
    [InlineData("player_api.php", "get_series", "get_series", true, ContentType.Series, false)]
    [InlineData("player_api.php", "get_vod_info", "get_vod_info", true, ContentType.Vod, false)]
    [InlineData("player_api.php", "get_series_info", "get_series_info", true, ContentType.Series, false)]
    [InlineData("player_api.php", "get_live_categories", "get_live_categories", false, null, false)]
    [InlineData("xmltv.php", "get_vod_categories", null, false, null, false)]
    public void Classify_RecognizesExpectedActions(string rest, string action, string? expectedAction, bool expectedTransformCandidate, ContentType? expectedContentType, bool expectedCategoryRewrite)
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["action"] = action
        });

        var result = _classifier.Classify(rest, query);

        Assert.Equal(rest.Equals("player_api.php", StringComparison.OrdinalIgnoreCase), result.IsPlayerApi);
        Assert.Equal(expectedAction, result.Action);
        Assert.Equal(expectedTransformCandidate, result.IsTransformCandidateAction);
        Assert.Equal(expectedContentType, result.ContentType);
        Assert.Equal(expectedCategoryRewrite, result.IsCategoryRewriteAction);
    }

    [Fact]
    public void Classify_PlayerApiWithoutAction_IsNotTransformCandidate()
    {
        var result = _classifier.Classify("player_api.php", new QueryCollection());

        Assert.True(result.IsPlayerApi);
        Assert.Null(result.Action);
        Assert.False(result.IsTransformCandidateAction);
        Assert.Null(result.ContentType);
        Assert.False(result.IsCategoryRewriteAction);
    }
}
