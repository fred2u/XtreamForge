namespace XtreamForge.Api.Services;

public sealed class XtreamRequestClassifier
{
    private static readonly HashSet<string> TransformCandidateActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_vod_categories",
        "get_series_categories",
        "get_vod_streams",
        "get_series",
        "get_vod_info",
        "get_series_info"
    };

    public XtreamRequestClassification Classify(string rest, IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!rest.Equals("player_api.php", StringComparison.OrdinalIgnoreCase))
        {
            return new XtreamRequestClassification(rest, false, null, false);
        }

        var action = query["action"].ToString();
        var normalizedAction = string.IsNullOrWhiteSpace(action) ? null : action;
        var isKnownAction = normalizedAction is not null && TransformCandidateActions.Contains(normalizedAction);

        return new XtreamRequestClassification(rest, true, normalizedAction, isKnownAction);
    }
}

public sealed record XtreamRequestClassification(
    string Rest,
    bool IsPlayerApi,
    string? Action,
    bool IsTransformCandidateAction)
{
    public string EndpointType => IsPlayerApi ? "player_api" : "other";
}
