using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using XtreamForge.ApiService.Options;

namespace XtreamForge.ApiService.Xtream;

public sealed record XtreamProviderValidationResult(string? Error = default)
{
    public bool IsValid => Error is null;

    public static XtreamProviderValidationResult Success() => new();
    public static XtreamProviderValidationResult Invalid(string error) => new(error);
}

public sealed class XtreamProviderValidator(IOptions<XtreamProxyOptions> options)
{
    private static readonly TimeSpan PublicHostCacheLifetime = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, CachedHostEvaluation> PublicHostCache = new(StringComparer.OrdinalIgnoreCase);

    public XtreamProviderValidationResult Validate(string protocol, string host, int port)
    {
        if (!IsSupportedProtocol(protocol))
            return XtreamProviderValidationResult.Invalid("Invalid protocol. Only http and https are supported.");

        if (!IsValidHost(host))
            return XtreamProviderValidationResult.Invalid("Invalid host.");

        if (port is < 1 or > 65535)
            return XtreamProviderValidationResult.Invalid("Port must be between 1 and 65535.");

        if (!IsAllowedHost(host))
            return XtreamProviderValidationResult.Invalid("Upstream host is not allowed.");

        return XtreamProviderValidationResult.Success();
    }

    private static bool IsSupportedProtocol(string protocol)
    {
        return protocol.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || protocol.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

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
                return IsPubliclyRoutableAddress(parsedAddress);

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
            return false;

        if (address.IsIPv4MappedToIPv6)
            return IsPubliclyRoutableAddress(address.MapToIPv4());

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var ipv6Bytes = address.GetAddressBytes();
            if (address.Equals(IPAddress.IPv6None)
                || address.Equals(IPAddress.IPv6Loopback)
                || address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal
                || address.IsIPv6Teredo
                || IsReservedIpv6Range(ipv6Bytes))
            {
                return false;
            }

            return (ipv6Bytes[0] & 0xfe) != 0xfc;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;

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

    private static bool IsReservedIpv6Range(byte[] bytes)
    {
        return bytes is
            [
                0x00, 0x64, 0xff, 0x9b, .. // 64:ff9b::/96
            ]
            or
            [
                0x01, 0x00, .. // 100::/64 discard-only
            ]
            or
            [
                0x20, 0x01, 0x0d, 0xb8, .. // 2001:db8::/32 documentation
            ]
            or
            [
                0x20, 0x02, .. // 2002::/16 6to4
            ];
    }

    private sealed record CachedHostEvaluation(bool IsAllowed, DateTimeOffset ExpiresAtUtc);
}
