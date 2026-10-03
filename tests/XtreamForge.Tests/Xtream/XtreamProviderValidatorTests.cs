using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.Tests.Xtream;

public class XtreamProviderValidatorTests
{
    [Theory]
    [InlineData("ftp")]
    [InlineData("file")]
    [InlineData("")]
    public void Validate_WithUnsupportedProtocol_IsInvalid(string protocol)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var result = validator.Validate(protocol, "provider.example.com", 80);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("HTTP")]
    [InlineData("https")]
    public void Validate_WithHttpOrHttpsInAnyCase_IsValid(string protocol)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var result = validator.Validate(protocol, "provider.example.com", 80);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad host")]
    [InlineData("::1")]
    public void Validate_WithInvalidHost_IsInvalid(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var result = validator.Validate("http", host, 80);

        Assert.Equal("Invalid host.", result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_WithPortOutOfRange_IsInvalid(int port)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var result = validator.Validate("http", "provider.example.com", port);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Validate_WithPortAtBoundary_IsValid(int port)
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var result = validator.Validate("http", "provider.example.com", port);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithAllowedHostInDifferentCase_IsValid()
    {
        var validator = CreateValidator(allowedHosts: ["Provider.Example.com"]);

        var result = validator.Validate("http", "provider.example.com", 80);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithHostNotAllowedAndAnyDestinationDisabled_IsInvalid()
    {
        var validator = CreateValidator(allowedHosts: ["provider.example.com"]);

        var result = validator.Validate("http", "8.8.8.8", 80);

        Assert.False(result.IsValid);
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
    public void Validate_WithAnyDestinationAndNonPublicAddress_IsInvalid(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var result = validator.Validate("http", host, 80);

        Assert.Equal("Upstream host is not allowed.", result.Error);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    public void Validate_WithAnyDestinationAndPublicAddress_IsValid(string host)
    {
        var validator = CreateValidator(allowAnyDestination: true);

        var result = validator.Validate("http", host, 80);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithExplicitlyAllowedPrivateAddress_IsValid()
    {
        var validator = CreateValidator(allowedHosts: ["192.168.1.10"]);

        var result = validator.Validate("http", "192.168.1.10", 80);

        Assert.True(result.IsValid);
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
