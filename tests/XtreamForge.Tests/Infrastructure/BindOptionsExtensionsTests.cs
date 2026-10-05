using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ApiService.Options;

namespace XtreamForge.Tests.Infrastructure;

public sealed class BindOptionsExtensionsTests
{
    [Theory]
    [InlineData("true", null)]
    [InlineData("false", "provider.example.com")]
    public void XtreamProxyOptions_WithAnAllowedDestination_AreValid(string allowAnyDestination, string? allowedHost)
    {
        using var provider = CreateProvider(new()
        {
            ["XtreamProxy:AllowAnyDestination"] = allowAnyDestination,
            ["XtreamProxy:AllowedHosts:0"] = allowedHost
        });

        var options = provider.GetRequiredService<IOptions<XtreamProxyOptions>>().Value;

        Assert.Equal(bool.Parse(allowAnyDestination), options.AllowAnyDestination);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void XtreamProxyOptions_WithoutAllowedDestination_AreRejected(string? allowedHost)
    {
        using var provider = CreateProvider(new()
        {
            ["XtreamProxy:AllowAnyDestination"] = "false",
            ["XtreamProxy:AllowedHosts:0"] = allowedHost
        });

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<XtreamProxyOptions>>().Value);

        Assert.Contains("XtreamProxy:AllowedHosts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PopularOptions_WithTheRecommendationsCategoryId_AreRejected()
    {
        using var provider = CreateProvider(new()
        {
            ["Recommendations:CategoryId"] = "999999999",
            ["Popular:CategoryId"] = "999999999"
        });

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PopularOptions>>().Value);

        Assert.Contains("Popular:CategoryId must differ from Recommendations:CategoryId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PopularOptions_WithADistinctCategoryId_AreValid()
    {
        using var provider = CreateProvider(new()
        {
            ["Recommendations:CategoryId"] = "999999999",
            ["Popular:CategoryId"] = "999999998"
        });

        Assert.Equal(999_999_998, provider.GetRequiredService<IOptions<PopularOptions>>().Value.CategoryId);
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBindedOptions();

        return services.BuildServiceProvider();
    }
}
