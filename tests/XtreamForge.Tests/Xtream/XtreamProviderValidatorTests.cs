using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ApiService.Infrastructure.RateLimiting;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Xtream;

public sealed class XtreamProviderValidatorTests : IDisposable
{
    private readonly TestMeterFactory _meterFactory = new();

    [Theory]
    [InlineData("ftp")]
    [InlineData("file")]
    [InlineData("")]
    public void Validate_WithUnsupportedProtocol_IsInvalid(string protocol)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var error = validator.Validate(protocol, "provider.example.com", 80);

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("HTTP")]
    [InlineData("https")]
    public void Validate_WithHttpOrHttpsInAnyCase_IsValid(string protocol)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var error = validator.Validate(protocol, "provider.example.com", 80);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad host")]
    [InlineData("::1")]
    public void Validate_WithInvalidHost_IsInvalid(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var error = validator.Validate("http", host, 80);

        Assert.Equal("Invalid host.", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_WithPortOutOfRange_IsInvalid(int port)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var error = validator.Validate("http", "provider.example.com", port);

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_WithPortAtBoundary_IsValid(int port)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var error = validator.Validate("http", "provider.example.com", port);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_WithAllowedHostInDifferentCase_IsValid()
    {
        var validator = CreateValidator(allowedHosts: ["Provider.Example.com"]);

        var error = validator.Validate("http", "provider.example.com", 80);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_WithHostNotAllowedAndAnyDestinationDisabled_IsInvalid()
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var error = validator.Validate("http", "8.8.8.8", 80);

        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    public void Validate_WithAnyDestinationAndNonPublicAddress_IsInvalid(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var error = validator.Validate("http", host, 80);

        Assert.Equal("Upstream host is not allowed.", error);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("192.0.1.1")]
    [InlineData("198.20.0.1")]
    [InlineData("223.255.255.255")]
    public void Validate_WithAnyDestinationAndPublicAddress_IsValid(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var error = validator.Validate("http", host, 80);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_WithExplicitlyAllowedPrivateAddress_IsValid()
    {
        var validator = CreateValidator(allowedHosts: ["192.168.1.10"]);

        var error = validator.Validate("http", "192.168.1.10", 80);

        Assert.Null(error);
    }

    [Fact]
    public void Validate_WithAnyDestinationAndHostName_DefersTheAddressCheckToTheConnection()
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var error = validator.Validate("http", "localhost", 80);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("224.0.0.1")]
    public async Task ResolveAllowedAddressesAsync_WithAnyDestinationAndNonPublicAddress_Throws(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => validator.ResolveAllowedAddressesAsync(host, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveAllowedAddressesAsync_WithAnyDestinationAndPublicAddress_ReturnsIt()
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var addresses = await validator.ResolveAllowedAddressesAsync("8.8.8.8", TestContext.Current.CancellationToken);

        Assert.Equal([IPAddress.Parse("8.8.8.8")], addresses);
    }

    [Fact]
    public async Task ResolveAllowedAddressesAsync_WithExplicitlyAllowedPrivateAddress_ReturnsIt()
    {
        var validator = CreateValidator(allowedHosts: ["192.168.1.10"]);

        var addresses = await validator.ResolveAllowedAddressesAsync("192.168.1.10", TestContext.Current.CancellationToken);

        Assert.Equal([IPAddress.Parse("192.168.1.10")], addresses);
    }

    [Fact]
    public async Task ResolveAllowedAddressesAsync_WithHostNotAllowedAndAnyDestinationDisabled_Throws()
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => validator.ResolveAllowedAddressesAsync("8.8.8.8", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task XtreamHttpClient_WithAnyDestination_DoesNotConnectToANonPublicAddress()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClients();
        services.AddSingleton(new UpstreamRateLimiter(new SteppingTimeProvider(), NullLogger<UpstreamRateLimiter>.Instance, _meterFactory));
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new XtreamProxyOptions { AllowAnyDestination = true }));
        await using var provider = services.BuildServiceProvider();

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(XtreamProxyOptions.HttpClientName);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("http://localhost:1/player_api.php", TestContext.Current.CancellationToken));

        var refusal = Assert.IsType<HttpRequestException>(exception.InnerException);
        Assert.Equal("Upstream host resolves to a non-public address.", refusal.Message);
    }

    public void Dispose()
    {
        _meterFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static XtreamProviderValidator CreateValidator(bool allowAnyDestination = false, string[]? allowedHosts = null)
    {
        var options = new XtreamProxyOptions
        {
            AllowAnyDestination = allowAnyDestination,
            AllowedHosts = allowedHosts ?? []
        };
        return new XtreamProviderValidator(Microsoft.Extensions.Options.Options.Create(options));
    }
}
