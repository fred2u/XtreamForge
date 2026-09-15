using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using XtreamForge.Configuration;
using XtreamForge.Xtream;

namespace XtreamForge.Tests;

public sealed class XtreamUpstreamDestinationResolverTests
{
    [Fact]
    public void Resolve_AllowsExplicitlyConfiguredIpv6Hosts()
    {
        var resolver = CreateResolver(new XtreamProxyOptions
        {
            AllowedHosts = ["2001:db8::1"]
        });

        var result = resolver.Resolve("https", "2001:db8::1", "443", "player_api.php", QueryString.Empty);

        Assert.True(result.IsValid);
        Assert.Equal("2001:db8::1", result.Destination?.Host);
    }

    [Fact]
    public void Resolve_AllowAnyDestinationRejectsNonPublicIpv6Hosts()
    {
        var resolver = CreateResolver(new XtreamProxyOptions
        {
            AllowAnyDestination = true
        });

        var result = resolver.Resolve("https", "fc00::1", "443", "player_api.php", QueryString.Empty);

        Assert.False(result.IsValid);
        Assert.Equal("Upstream host is not allowed.", result.Error);
    }

    private static XtreamUpstreamDestinationResolver CreateResolver(XtreamProxyOptions options) =>
        new(Options.Create(options));
}
