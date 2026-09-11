using System.Net;

namespace XtreamForge.Api.Services;

public sealed class XtreamUpstreamDestinationResolver
{
    public UpstreamResolutionResult Resolve(
        string protocol,
        string host,
        string port,
        string? rest,
        QueryString queryString)
    {
        if (!IsSupportedProtocol(protocol))
        {
            return UpstreamResolutionResult.Invalid("Invalid protocol. Only http and https are supported.");
        }

        if (!IsValidHost(host))
        {
            return UpstreamResolutionResult.Invalid("Invalid host.");
        }

        if (!int.TryParse(port, out var parsedPort))
        {
            return UpstreamResolutionResult.Invalid("Invalid port.");
        }

        if (parsedPort is < 1 or > 65535)
        {
            return UpstreamResolutionResult.Invalid("Port must be between 1 and 65535.");
        }

        var normalizedRest = NormalizeRestPath(rest);
        var targetUri = BuildTargetUri(protocol, host, parsedPort, normalizedRest, queryString);

        return UpstreamResolutionResult.Success(new XtreamUpstreamDestination(protocol, host, parsedPort, normalizedRest, targetUri));
    }

    private static bool IsSupportedProtocol(string protocol) =>
        protocol.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || protocol.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4;
    }

    private static string NormalizeRestPath(string? rest) => (rest ?? string.Empty).Trim('/');

    private static Uri BuildTargetUri(string protocol, string host, int port, string rest, QueryString queryString)
    {
        var encodedPath = string.IsNullOrEmpty(rest)
            ? "/"
            : "/" + string.Join('/', rest.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

        var uriBuilder = new UriBuilder(protocol, host, port)
        {
            Path = encodedPath,
            Query = queryString.Value is ['?', .. var query] ? query : string.Empty
        };

        return uriBuilder.Uri;
    }
}

public sealed record XtreamUpstreamDestination(
    string Protocol,
    string Host,
    int Port,
    string Rest,
    Uri TargetUri);

public sealed record UpstreamResolutionResult(XtreamUpstreamDestination? Destination, string? Error)
{
    public bool IsValid => Error is null;

    public static UpstreamResolutionResult Success(XtreamUpstreamDestination destination) => new(destination, null);

    public static UpstreamResolutionResult Invalid(string error) => new(null, error);
}
