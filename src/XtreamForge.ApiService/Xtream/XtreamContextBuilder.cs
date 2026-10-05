using System.Diagnostics.CodeAnalysis;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Xtream;

public sealed class XtreamContextBuilder(XtreamProviderValidator xtreamProviderValidator)
{
    /// <summary>
    /// Validates the upstream destination and builds the context of the request; returns false with the validation
    /// <paramref name="error"/> when the destination is rejected.
    /// </summary>
    public bool TryBuild(
        string protocol, string host, int port, string rest, HttpContext httpContext,
        [NotNullWhen(true)] out XtreamContext? xtreamContext,
        [NotNullWhen(false)] out string? error)
    {
        error = xtreamProviderValidator.Validate(protocol, host, port);
        if (error is not null)
        {
            xtreamContext = null;
            return false;
        }

        var normalizedRest = rest.Trim('/');
        var (requestAction, contentType) = Classify(normalizedRest, httpContext.Request.Query);

        xtreamContext = new XtreamContext(protocol.ToLowerInvariant(), host.ToLowerInvariant(), port, normalizedRest, httpContext, requestAction, contentType);
        return true;
    }

    private static (RequestAction, ContentType) Classify(string rest, IQueryCollection query)
    {
        if (!rest.Equals("player_api.php", StringComparison.InvariantCultureIgnoreCase))
            return (RequestAction.Undefined, ContentType.Undefined);

        return query["action"].ToString().Trim().ToLowerInvariant() switch
        {
            "" => (RequestAction.Authenticate, ContentType.Undefined),
            "get_vod_categories" => (RequestAction.GetCategories, ContentType.Vod),
            "get_series_categories" => (RequestAction.GetCategories, ContentType.Series),
            "get_vod_streams" => (RequestAction.GetItems, ContentType.Vod),
            "get_series" => (RequestAction.GetItems, ContentType.Series),
            "get_vod_info" => (RequestAction.GetInfo, ContentType.Vod),
            "get_series_info" => (RequestAction.GetInfo, ContentType.Series),
            _ => (RequestAction.Undefined, ContentType.Undefined)
        };
    }
}
