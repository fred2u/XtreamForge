namespace XtreamForge.ApiService.Options;

/// <summary>
/// Reverse proxies whose <c>X-Forwarded-For</c>, <c>X-Forwarded-Proto</c>, and <c>X-Forwarded-Host</c> headers are trusted,
/// in addition to the loopback addresses.
/// </summary>
public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>IP addresses of the trusted reverse proxies, for example <c>10.0.0.5</c>.</summary>
    public string[] KnownProxies { get; init; } = [];

    /// <summary>Networks of the trusted reverse proxies in CIDR notation, for example <c>172.18.0.0/16</c>.</summary>
    public string[] KnownNetworks { get; init; } = [];
}
