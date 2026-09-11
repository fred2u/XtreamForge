using XtreamForge.Api.Services;

namespace XtreamForge.Api.Endpoints;

public static class XtreamEndpointExtensions
{
    public static IServiceCollection AddXtreamEndpoints(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(ForwarderService.HttpClientName);
        services.AddScoped<ForwarderService>();
        services.AddScoped<XtreamPlayerApiHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapXtreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapMethods(
                "/{protocol}/{host}/{port}",
                SupportedHttpMethods,
                HandleXtreamRequestAsync)
            .WithSummary("Handles or forwards Xtream-compatible requests.");

        endpoints.MapMethods(
                "/{protocol}/{host}/{port}/{**rest}",
                SupportedHttpMethods,
                HandleXtreamRequestAsync)
            .WithName("HandleXtreamRequest")
            .WithSummary("Handles or forwards Xtream-compatible requests.");

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
        XtreamPlayerApiHandler xtreamPlayerApiHandler,
        ForwarderService forwarderService)
    {
        if (!IsSupportedProtocol(protocol))
        {
            return TypedResults.BadRequest("Invalid protocol. Only http and https are supported.");
        }

        if (!int.TryParse(port, out var parsedPort) || parsedPort is < 1 or > 65535)
        {
            return TypedResults.BadRequest("Invalid port.");
        }

        var normalizedRest = NormalizeRestPath(rest);

        if (await xtreamPlayerApiHandler.TryHandleAsync(normalizedRest, context, context.RequestAborted) is { } localResult)
        {
            return localResult;
        }

        var targetUri = BuildTargetUri(protocol, host, parsedPort, normalizedRest, context.Request.QueryString);
        return await forwarderService.ForwardAsync(targetUri, context);
    }

    private static bool IsSupportedProtocol(string protocol) =>
        protocol.Equals("http", StringComparison.OrdinalIgnoreCase)
        || protocol.Equals("https", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRestPath(string? rest) => (rest ?? string.Empty).Trim('/');

    private static Uri BuildTargetUri(string protocol, string host, int port, string rest, QueryString queryString)
    {
        var uriBuilder = new UriBuilder(protocol, host, port)
        {
            Path = string.IsNullOrEmpty(rest) ? string.Empty : rest,
            Query = queryString.Value is ['?', .. var query] ? query : string.Empty
        };

        return uriBuilder.Uri;
    }
}
