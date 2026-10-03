using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Xtream;

public sealed record XtreamContextBuildResult(XtreamContext? XtreamContext, string? Error)
{
    public bool IsValid => Error is null;

    public static XtreamContextBuildResult Success(XtreamContext xtreamContext) => new(xtreamContext, null);
    public static XtreamContextBuildResult Invalid(string error) => new(null, error);
}

public sealed class XtreamContextBuilder(XtreamProviderValidator xtreamProviderValidator)
{
    public XtreamContextBuildResult Build(string protocol, string host, int port, string rest, HttpContext httpContext)
    {
        var providerValidationResult = xtreamProviderValidator.Validate(protocol, host, port);

        if (!providerValidationResult.IsValid)
            return XtreamContextBuildResult.Invalid(providerValidationResult.Error!);

        var normalizedProtocol = protocol.ToLowerInvariant();
        var normalizedHost = host.ToLowerInvariant();
        var normalizedRest = rest.Trim('/');

        var (requestAction, contentType) = Classify(normalizedRest, httpContext.Request.Query);

        var xtreamContext = new XtreamContext(normalizedProtocol, normalizedHost, port, normalizedRest, httpContext, requestAction, contentType);

        return XtreamContextBuildResult.Success(xtreamContext);
    }

    private static (RequestAction, ContentType) Classify(string rest, IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!rest.Equals("player_api.php", StringComparison.InvariantCultureIgnoreCase))
            return (RequestAction.Undefined, ContentType.Undefined);

        var normalizedAction = query["action"].ToString().Trim().ToLowerInvariant();

        RequestAction requestAction;
        ContentType contentType;

        switch (normalizedAction)
        {
            case "":
                {
                    requestAction = RequestAction.Authenticate;
                    contentType = ContentType.Undefined;
                    break;
                }

            case "get_vod_categories":
                {
                    requestAction = RequestAction.GetCategories;
                    contentType = ContentType.Vod;
                    break;
                }

            case "get_series_categories":
                {
                    requestAction = RequestAction.GetCategories;
                    contentType = ContentType.Series;
                    break;
                }

            case "get_vod_streams":
                {
                    requestAction = RequestAction.GetItems;
                    contentType = ContentType.Vod;
                    break;
                }

            case "get_series":
                {
                    requestAction = RequestAction.GetItems;
                    contentType = ContentType.Series;
                    break;
                }

            case "get_vod_info":
                {
                    requestAction = RequestAction.GetInfo;
                    contentType = ContentType.Vod;
                    break;
                }

            case "get_series_info":
                {
                    requestAction = RequestAction.GetInfo;
                    contentType = ContentType.Series;
                    break;
                }

            default:
                {
                    requestAction = RequestAction.Undefined;
                    contentType = ContentType.Undefined;
                    break;
                }
        }

        return (requestAction, contentType);
    }
}
