namespace XtreamForge.Api.Services;

public sealed class XtreamPlayerApiHandler
{
    private static readonly HashSet<string> SupportedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_vod_categories",
        "get_series_categories",
        "get_vod_streams",
        "get_series",
        "get_vod_info",
        "get_series_info"
    };

    public Task<IResult?> TryHandleAsync(string rest, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (!rest.Equals("player_api.php", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IResult?>(null);
        }

        var action = context.Request.Query["action"].ToString();
        if (string.IsNullOrWhiteSpace(action) || !SupportedActions.Contains(action))
        {
            return Task.FromResult<IResult?>(null);
        }

        context.Response.Headers.Append("X-XtreamForge-Intercepted", "true");

        return Task.FromResult<IResult?>(TypedResults.Json(
            new XtreamInterceptedResponse(
                action,
                "NotImplemented",
                "This Xtream action is recognized locally but its business logic is not implemented yet."),
            statusCode: StatusCodes.Status501NotImplemented));
    }

    private sealed record XtreamInterceptedResponse(string Action, string Status, string Message);
}
