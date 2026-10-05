using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.ServiceDefaults;

namespace XtreamForge.ApiService.Endpoints.Xtream;

public static class RouteExtensions
{
    private const string PlayerApiPath = "player_api.php";

    // stream paths built by clients from the rewritten server_info: {kind}/{username}/{password}/{file}
    private static readonly string[] AccountStreamKinds = ["movie", "series", "live"];

    private static readonly string[] SupportedHttpMethods =
    [
        HttpMethods.Get,
        HttpMethods.Head
    ];

    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapXtreamEndpoints()
        {
            // the literal player_api.php segment takes precedence over the catch-all route,
            // so stream and other requests only resolve the services needed to forward them
            endpoints
                .MapMethods($"/{{protocol}}/{{host}}/{{port}}/{PlayerApiPath}", SupportedHttpMethods, HandlePlayerApiRequestAsync)
                .WithName("HandleXtreamPlayerApiRequest")
                .ExcludeFromDescription();

            // the short live form {username}/{password}/{streamId} has no stream kind: its credentials are only recognized
            // from the route values, which the telemetry and the upstream request logs redact; it is forwarded like any other path
            endpoints
                .MapMethods(
                    $"/{{protocol}}/{{host}}/{{port}}/{{{XtreamCredentialRedaction.UsernameRouteValue}}}/{{{XtreamCredentialRedaction.PasswordRouteValue}}}/{{streamId}}",
                    SupportedHttpMethods,
                    ForwardShortLiveStreamAsync)
                .WithName("ForwardXtreamShortLiveStream")
                .ExcludeFromDescription();

            endpoints
                .MapMethods("/{protocol}/{host}/{port}/{**rest}", SupportedHttpMethods, ForwardXtreamRequestAsync)
                .WithName("ForwardXtreamRequest")
                .ExcludeFromDescription();

            // the literal first segment takes precedence over the {protocol} parameter of the forward route
            foreach (var kind in AccountStreamKinds)
            {
                endpoints
                    .MapMethods($"/{kind}/{{username}}/{{password}}/{{file}}", SupportedHttpMethods, (string username, string password, string file, HttpContext httpContext, XtreamAccountDirectory accountDirectory, XtreamRequestForwardEndpoint xtreamRequestForwardEndpoint, XtreamContextBuilder contextBuilder)
                        => ForwardAccountStreamAsync(kind, username, password, file, httpContext, accountDirectory, xtreamRequestForwardEndpoint, contextBuilder))
                    .WithName($"ForwardXtreamAccountStream_{kind}")
                    .ExcludeFromDescription();
            }

            return endpoints;
        }
    }

    private static async Task<IResult> HandlePlayerApiRequestAsync(
       string protocol, string host, int port,
       HttpContext httpContext,
       AuthenticateEndpoint authenticateEndpoint,
       CategoriesGetEndpoint categoriesGetEndpoint,
       ItemsGetEndpoint itemsGetEndpoint,
       ItemGetEndpoint itemGetEndpoint,
       XtreamRequestForwardEndpoint xtreamRequestForwardEndpoint,
       XtreamContextBuilder contextBuilder)
    {
        if (!contextBuilder.TryBuild(protocol, host, port, PlayerApiPath, httpContext, out var xtreamContext, out var error))
        {
            return TypedResults.BadRequest(error);
        }

        return xtreamContext.Action switch
        {
            RequestAction.Authenticate when HttpMethods.IsGet(httpContext.Request.Method) => await authenticateEndpoint.GetAsync(xtreamContext, httpContext.RequestAborted),
            RequestAction.GetCategories => await categoriesGetEndpoint.GetAsync(xtreamContext, httpContext.RequestAborted),
            RequestAction.GetItems => await itemsGetEndpoint.GetAsync(xtreamContext, httpContext.RequestAborted),
            RequestAction.GetInfo => await itemGetEndpoint.GetAsync(xtreamContext, httpContext.RequestAborted),
            _ => await xtreamRequestForwardEndpoint.ForwardAsync(xtreamContext, httpContext.RequestAborted)
        };
    }

    private static async Task<IResult> ForwardXtreamRequestAsync(
       string protocol, string host, int port, string rest,
       HttpContext httpContext,
       XtreamRequestForwardEndpoint xtreamRequestForwardEndpoint,
       XtreamContextBuilder contextBuilder)
    {
        if (!contextBuilder.TryBuild(protocol, host, port, rest, httpContext, out var xtreamContext, out var error))
        {
            return TypedResults.BadRequest(error);
        }

        return await xtreamRequestForwardEndpoint.ForwardAsync(xtreamContext, httpContext.RequestAborted);
    }

    private static Task<IResult> ForwardShortLiveStreamAsync(
       string protocol, string host, int port, string username, string password, string streamId,
       HttpContext httpContext,
       XtreamRequestForwardEndpoint xtreamRequestForwardEndpoint,
       XtreamContextBuilder contextBuilder)
        => ForwardXtreamRequestAsync(protocol, host, port, $"{username}/{password}/{streamId}", httpContext, xtreamRequestForwardEndpoint, contextBuilder);

    // the upstream of a stream path without destination is the one of its account, known once the account has authenticated
    private static async Task<IResult> ForwardAccountStreamAsync(
       string kind, string username, string password, string file,
       HttpContext httpContext,
       XtreamAccountDirectory accountDirectory,
       XtreamRequestForwardEndpoint xtreamRequestForwardEndpoint,
       XtreamContextBuilder contextBuilder)
    {
        if (accountDirectory.Find(username, password) is not { } upstream)
        {
            return TypedResults.NotFound();
        }

        if (!contextBuilder.TryBuild(upstream.Protocol, upstream.Host, upstream.Port, $"{kind}/{username}/{password}/{file}", httpContext, out var xtreamContext, out var error))
        {
            return TypedResults.BadRequest(error);
        }

        return await xtreamRequestForwardEndpoint.ForwardAsync(xtreamContext, httpContext.RequestAborted);
    }
}
