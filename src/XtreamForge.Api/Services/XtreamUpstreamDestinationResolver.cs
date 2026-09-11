using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using XtreamForge.Api.Configuration;

namespace XtreamForge.Api.Services;

public sealed class XtreamUpstreamDestinationResolver(IOptions<XtreamProxyOptions> options)
{
    private static readonly TimeSpan PublicHostCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, CachedHostEvaluation> PublicHostCache = new(StringComparer.OrdinalIgnoreCase);

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

        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedPort))
        {
            return UpstreamResolutionResult.Invalid("Invalid port.");
        }

        if (parsedPort is < 1 or > 65535)
        {
            return UpstreamResolutionResult.Invalid("Port must be between 1 and 65535.");
        }

        if (!IsAllowedHost(host))
        {
            return UpstreamResolutionResult.Invalid("Upstream host is not allowed.");
        }

        var normalizedProtocol = protocol.ToLowerInvariant();
        var normalizedHost = host.ToLowerInvariant();
        var normalizedRest = NormalizeRestPath(rest);
        var targetUri = BuildTargetUri(normalizedProtocol, normalizedHost, parsedPort, normalizedRest, queryString);

        return UpstreamResolutionResult.Success(new XtreamUpstreamDestination(normalizedProtocol, normalizedHost, parsedPort, normalizedRest, targetUri));
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

    private bool IsAllowedHost(string host)
    {
        var proxyOptions = options.Value;
        if (proxyOptions.AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return proxyOptions.AllowAnyDestination && IsPubliclyRoutableHost(host);
    }

    private static bool IsPubliclyRoutableHost(string host)
    {
        if (PublicHostCache.TryGetValue(host, out var cachedEvaluation)
            && cachedEvaluation.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return cachedEvaluation.IsAllowed;
        }

        var isAllowed = EvaluatePubliclyRoutableHost(host);
        PublicHostCache[host] = new CachedHostEvaluation(isAllowed, DateTimeOffset.UtcNow.Add(PublicHostCacheLifetime));
        return isAllowed;
    }

    private static bool EvaluatePubliclyRoutableHost(string host)
    {
        try
        {
            if (IPAddress.TryParse(host, out var parsedAddress))
            {
                return IsPubliclyRoutableAddress(parsedAddress);
            }

            var addresses = Dns.GetHostAddresses(host);
            return addresses.Length > 0 && addresses.All(IsPubliclyRoutableAddress);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPubliclyRoutableAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPubliclyRoutableAddress(address.MapToIPv4());
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Loopback)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal)
            {
                return false;
            }

            var ipv6Bytes = address.GetAddressBytes();
            return (ipv6Bytes[0] & 0xfe) != 0xfc;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            0 => false,
            10 => false,
            100 when bytes[1] >= 64 && bytes[1] <= 127 => false,
            127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
            192 when bytes[1] == 168 => false,
            _ => true
        };
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

    private sealed record CachedHostEvaluation(bool IsAllowed, DateTimeOffset ExpiresAtUtc);
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
