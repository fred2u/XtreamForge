using Microsoft.AspNetCore.Http;
using XtreamForge.Api.Services;

namespace XtreamForge.Api.Tests;

public sealed class XtreamRequestClassifierTests
{
    private readonly XtreamRequestClassifier _classifier = new();

    [Theory]
    [InlineData("player_api.php", "get_vod_categories", "get_vod_categories", true)]
    [InlineData("player_api.php", "get_series_categories", "get_series_categories", true)]
    [InlineData("player_api.php", "get_vod_streams", "get_vod_streams", true)]
    [InlineData("player_api.php", "get_series", "get_series", true)]
    [InlineData("player_api.php", "get_vod_info", "get_vod_info", true)]
    [InlineData("player_api.php", "get_series_info", "get_series_info", true)]
    [InlineData("player_api.php", "get_live_categories", "get_live_categories", false)]
    [InlineData("xmltv.php", "get_vod_categories", null, false)]
    public void Classify_RecognizesExpectedActions(string rest, string action, string? expectedAction, bool expectedTransformCandidate)
    {
        var query = new QueryCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["action"] = action
        });

        var result = _classifier.Classify(rest, query);

        Assert.Equal(rest.Equals("player_api.php", StringComparison.OrdinalIgnoreCase), result.IsPlayerApi);
        Assert.Equal(expectedAction, result.Action);
        Assert.Equal(expectedTransformCandidate, result.IsTransformCandidateAction);
    }

    [Fact]
    public void Classify_PlayerApiWithoutAction_IsNotTransformCandidate()
    {
        var result = _classifier.Classify("player_api.php", new QueryCollection());

        Assert.True(result.IsPlayerApi);
        Assert.Null(result.Action);
        Assert.False(result.IsTransformCandidateAction);
    }
}
