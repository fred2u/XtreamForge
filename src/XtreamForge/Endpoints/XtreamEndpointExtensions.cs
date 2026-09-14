using XtreamForge.Xtream;

namespace XtreamForge.Endpoints;

public static class XtreamEndpointExtensions
{
    public static IEndpointRouteBuilder MapXtreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapMethods(
                "/{protocol}/{host}/{port}",
                SupportedHttpMethods,
                HandleXtreamRequestAsync)
            .ExcludeFromDescription();

        endpoints.MapMethods(
                "/{protocol}/{host}/{port}/{**rest}",
                SupportedHttpMethods,
                HandleXtreamRequestAsync)
            .WithName("HandleXtreamRequest")
            .ExcludeFromDescription();

        return endpoints;
    }

    private static readonly string[] SupportedHttpMethods =
    [
        HttpMethods.Get,
        HttpMethods.Head,
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete,
        HttpMethods.Options
    ];

    private static async Task<IResult> HandleXtreamRequestAsync(
        string protocol,
        string host,
        string port,
        string? rest,
        HttpContext context,
        XtreamUpstreamDestinationResolver destinationResolver,
        XtreamRequestClassifier requestClassifier,
        XtreamCategoryProxyService categoryProxyService,
        XtreamContentProxyService contentProxyService,
        ForwarderService forwarderService)
    {
        var resolutionResult = destinationResolver.Resolve(protocol, host, port, rest, context.Request.QueryString);
        if (!resolutionResult.IsValid)
        {
            return TypedResults.BadRequest(resolutionResult.Error);
        }

        var destination = resolutionResult.Destination;
        if (destination is null)
        {
            return TypedResults.BadRequest("Invalid upstream destination.");
        }

        var classification = requestClassifier.Classify(destination.Rest, context.Request.Query);
        if (await categoryProxyService.TryHandleAsync(destination, classification, context) is { } categoryResult)
        {
            return categoryResult;
        }

        if (await contentProxyService.TryHandleAsync(destination, classification, context) is { } contentResult)
        {
            return contentResult;
        }

        return await forwarderService.ForwardAsync(destination, classification, context);
    }
}
