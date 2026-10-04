using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using XtreamForge.ApiService.Options;

namespace XtreamForge.ApiService.Xtream;

public sealed record XtreamProviderValidationResult(string? Error = default)
{
    public bool IsValid => Error is null;

    public static XtreamProviderValidationResult Success() => new();
    public static XtreamProviderValidationResult Invalid(string error) => new(error);
}

/// <summary>
/// Upstream destination guard (SSRF). <see cref="Validate"/> checks the request without resolving the host;
/// <see cref="ConnectAsync"/>, the connect callback of the Xtream HTTP client, checks the addresses actually
/// connected to, so that a host cannot resolve to a public address when validated and to a private one when connected.
/// </summary>
public sealed class XtreamProviderValidator(IOptions<XtreamProxyOptions> options)
{
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

    /// <summary>
    /// Resolves the upstream host and returns the addresses it may be connected to: all of them for an explicitly
    /// allowed host, otherwise only when <see cref="XtreamProxyOptions.AllowAnyDestination"/> is set and every
    /// address is publicly routable.
    /// </summary>
    /// <exception cref="HttpRequestException">The host is not allowed or resolves to a non-public address.</exception>
    public async Task<IPAddress[]> ResolveAllowedAddressesAsync(string host, CancellationToken cancellationToken)
    {
        var proxyOptions = options.Value;
        var isExplicitlyAllowed = IsExplicitlyAllowedHost(host);
        if (!isExplicitlyAllowed && !proxyOptions.AllowAnyDestination)
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Upstream host is not allowed.");

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (!isExplicitlyAllowed && (addresses.Length == 0 || !addresses.All(IsPubliclyRoutableAddress)))
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Upstream host resolves to a non-public address.");

        return addresses;
    }

    /// <summary>Connect callback of the Xtream HTTP client: connects only to addresses allowed by <see cref="ResolveAllowedAddressesAsync"/>.</summary>
    public async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await ResolveAllowedAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private bool IsExplicitlyAllowedHost(string host) =>
        options.Value.AllowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    private bool IsAllowedHost(string host)
    {
        if (IsExplicitlyAllowedHost(host))
        {
            return true;
        }

        if (!options.Value.AllowAnyDestination)
        {
            return false;
        }

        // a host name is resolved and checked when connecting (ConnectAsync), an IP address can be rejected now
        return !IPAddress.TryParse(host, out var address) || IsPubliclyRoutableAddress(address);
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
            192 when bytes[1] == 0 && bytes[2] is 0 or 2 => false, // 192.0.0.0/24 IETF, 192.0.2.0/24 documentation
            192 when bytes[1] == 168 => false,
            198 when bytes[1] is 18 or 19 => false, // 198.18.0.0/15 benchmarking
            198 when bytes[1] == 51 && bytes[2] == 100 => false, // 198.51.100.0/24 documentation
            203 when bytes[1] == 0 && bytes[2] == 113 => false, // 203.0.113.0/24 documentation
            >= 224 => false, // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved and broadcast
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
}
