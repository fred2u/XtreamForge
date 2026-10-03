using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class SourcesPostEndpointTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;

    public SourcesPostEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    [Fact]
    public async Task PostAsync_WithValidUrl_ReturnsCreatedAndPersistsNormalizedSourceWithCategories()
    {
        var endpoint = CreateEndpoint(CreateSuccessfulHttpClientFactory());
        var request = new XtreamSourceCreateRequest("HTTP://Provider.Example.com:8080/", "user", "secret");

        var result = await endpoint.PostAsync(request, TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<XtreamSourceDto>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal($"/api/admin/sources/{created.Value.Id}", created.Location);
        var source = await _dbContext.XtreamSources.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(created.Value.Id, source.Id);
        Assert.Equal("http", source.Protocol);
        Assert.Equal("provider.example.com", source.Host);
        Assert.Equal(8080, source.Port);
        Assert.Equal(2, await _dbContext.XtreamCategories.CountAsync(c => c.XtreamSourceId == source.Id, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("http://provider.example.com", 80)]
    [InlineData("https://provider.example.com", 443)]
    public async Task PostAsync_WithoutExplicitPort_UsesSchemeDefaultPort(string url, int expectedPort)
    {
        var endpoint = CreateEndpoint(CreateSuccessfulHttpClientFactory());

        var result = await endpoint.PostAsync(new XtreamSourceCreateRequest(url, "user", "secret"), TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<XtreamSourceDto>>(result);
        Assert.Equal(expectedPort, created.Value?.Port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://provider.example.com")]
    [InlineData("http://not-allowed.example.com")]
    public async Task PostAsync_WithInvalidUrl_ReturnsValidationProblemAndDoesNotCallUpstream(string url)
    {
        var httpClientFactory = CreateSuccessfulHttpClientFactory();
        var endpoint = CreateEndpoint(httpClientFactory);

        var result = await endpoint.PostAsync(new XtreamSourceCreateRequest(url, "user", "secret"), TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(nameof(XtreamSourceCreateRequest.Url), problem.ProblemDetails.Errors.Keys);
        Assert.Empty(httpClientFactory.RequestedUris);
        Assert.False(await _dbContext.XtreamSources.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", "secret", nameof(XtreamSourceCreateRequest.Username))]
    [InlineData("user", " ", nameof(XtreamSourceCreateRequest.Password))]
    public async Task PostAsync_WithMissingCredentials_ReturnsValidationProblem(string username, string password, string expectedErrorKey)
    {
        var endpoint = CreateEndpoint(CreateSuccessfulHttpClientFactory());

        var result = await endpoint.PostAsync(new XtreamSourceCreateRequest("http://provider.example.com", username, password), TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(expectedErrorKey, problem.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task PostAsync_WhenSourceAlreadyExists_ReturnsConflictAndDoesNotCallUpstream()
    {
        _dbContext.XtreamSources.Add(new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var httpClientFactory = CreateSuccessfulHttpClientFactory();
        var endpoint = CreateEndpoint(httpClientFactory);

        var result = await endpoint.PostAsync(new XtreamSourceCreateRequest("http://PROVIDER.example.com:8080", "user", "secret"), TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
        Assert.Empty(httpClientFactory.RequestedUris);
        Assert.Equal(1, await _dbContext.XtreamSources.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostAsync_WhenUpstreamFails_ReturnsBadGatewayAndSavesNothing()
    {
        var httpClientFactory = new StubXtreamHttpClientFactory(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_categories"] = (HttpStatusCode.OK, "[]"),
            ["get_series_categories"] = (HttpStatusCode.Unauthorized, string.Empty)
        });
        var endpoint = CreateEndpoint(httpClientFactory);

        var result = await endpoint.PostAsync(new XtreamSourceCreateRequest("http://provider.example.com", "user", "secret"), TestContext.Current.CancellationToken);

        var statusCode = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, statusCode.StatusCode);
        Assert.False(await _dbContext.XtreamSources.AnyAsync(TestContext.Current.CancellationToken));
    }

    private SourcesPostEndpoint CreateEndpoint(IHttpClientFactory httpClientFactory)
    {
        var discoveryService = new XtreamCategoryDiscoveryAdminService(httpClientFactory, new CategoryService(_dbContext), NullLogger<XtreamCategoryDiscoveryAdminService>.Instance);
        var validator = new XtreamProviderValidator(Microsoft.Extensions.Options.Options.Create(new XtreamProxyOptions { AllowedHosts = ["provider.example.com"] }));

        return new SourcesPostEndpoint(new SourceAdminService(_dbContext), discoveryService, validator);
    }

    private static StubXtreamHttpClientFactory CreateSuccessfulHttpClientFactory() =>
        new(new Dictionary<string, (HttpStatusCode, string)>
        {
            ["get_vod_categories"] = (HttpStatusCode.OK, """[{"category_id":"1","category_name":"Action"}]"""),
            ["get_series_categories"] = (HttpStatusCode.OK, """[{"category_id":"2","category_name":"Drama"}]""")
        });

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
