using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Catalog;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.History;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class SourceEndpointsTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SourceAdminService _sourceAdminService;

    public SourceEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _sourceAdminService = new SourceAdminService(_dbContext);
    }

    // ─── GET /api/admin/sources ─────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_WhenSourcesExist_ReturnsOkWithExpectedSources()
    {
        _dbContext.XtreamSources.AddRange(
            new XtreamSource { Protocol = "http", Host = "source1.example.com", Port = 8080 },
            new XtreamSource { Protocol = "https", Host = "source2.example.com", Port = 443 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await SourceEndpoints.GetAsync(_sourceAdminService, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<XtreamSourceSummaryDto>>>(result);
        Assert.NotNull(ok.Value);
        var dtos = ok.Value.ToList();
        Assert.Equal(2, dtos.Count);
        Assert.Contains(dtos, d => d.Host == "source1.example.com" && d.Protocol == "http" && d.Port == 8080);
        Assert.Contains(dtos, d => d.Host == "source2.example.com" && d.Protocol == "https" && d.Port == 443);
    }

    [Fact]
    public async Task GetAsync_WhenNoSourcesExist_ReturnsOkWithEmptyCollection()
    {
        var result = await SourceEndpoints.GetAsync(_sourceAdminService, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<XtreamSourceSummaryDto>>>(result);
        Assert.NotNull(ok.Value);
        var dtos = ok.Value.ToList();
        Assert.Empty(dtos);
    }

    // ─── POST /api/admin/sources ────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_WithValidUrl_ReturnsCreatedAndPersistsNormalizedSourceWithCategories()
    {
        var request = new XtreamSourceCreateRequest("HTTP://Provider.Example.com:8080/", "user", "secret");

        var result = await PostAsync(CreateSuccessfulHttpClientFactory(), request);

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
        var result = await PostAsync(CreateSuccessfulHttpClientFactory(), new XtreamSourceCreateRequest(url, "user", "secret"));

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

        var result = await PostAsync(httpClientFactory, new XtreamSourceCreateRequest(url, "user", "secret"));

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
        var result = await PostAsync(CreateSuccessfulHttpClientFactory(), new XtreamSourceCreateRequest("http://provider.example.com", username, password));

        var problem = Assert.IsType<ValidationProblem>(result);
        Assert.Contains(expectedErrorKey, problem.ProblemDetails.Errors.Keys);
    }

    [Fact]
    public async Task PostAsync_WhenSourceAlreadyExists_ReturnsConflictAndDoesNotCallUpstream()
    {
        _dbContext.XtreamSources.Add(new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var httpClientFactory = CreateSuccessfulHttpClientFactory();

        var result = await PostAsync(httpClientFactory, new XtreamSourceCreateRequest("http://PROVIDER.example.com:8080", "user", "secret"));

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

        var result = await PostAsync(httpClientFactory, new XtreamSourceCreateRequest("http://provider.example.com", "user", "secret"));

        var statusCode = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, statusCode.StatusCode);
        Assert.False(await _dbContext.XtreamSources.AnyAsync(TestContext.Current.CancellationToken));
    }

    // ─── DELETE /api/admin/sources/{id} ─────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenSourceExists_ReturnsNoContent()
    {
        var source = new XtreamSource { Protocol = "http", Host = "source.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await SourceEndpoints.DeleteAsync(source.Id, _sourceAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceExists_SourceIsRemovedFromDatabase()
    {
        var source = new XtreamSource { Protocol = "http", Host = "source.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await SourceEndpoints.DeleteAsync(source.Id, _sourceAdminService, TestContext.Current.CancellationToken);

        var remaining = await _sourceAdminService.GetAllAsync(TestContext.Current.CancellationToken);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await SourceEndpoints.DeleteAsync(999, _sourceAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceHasRelatedData_CascadesDeleteToRelatedEntities()
    {
        var source = new XtreamSource { Protocol = "http", Host = "source.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        _dbContext.CategoryRules.Add(new CategoryRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "sport",
            IsEnabled = true
        });
        _dbContext.ItemRules.Add(new ItemRule
        {
            XtreamSourceId = source.Id,
            ContentType = ContentType.Vod,
            Sequence = 1,
            Action = RuleAction.Include,
            Operator = RuleOperator.Contains,
            Pattern = "news",
            IsEnabled = true
        });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await SourceEndpoints.DeleteAsync(source.Id, _sourceAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        Assert.False(await _dbContext.CategoryRules.AnyAsync(r => r.XtreamSourceId == source.Id, TestContext.Current.CancellationToken));
        Assert.False(await _dbContext.ItemRules.AnyAsync(r => r.XtreamSourceId == source.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceHasWatchHistory_KeepsThePlaybacksWithoutSource()
    {
        var source = new XtreamSource { Protocol = "http", Host = "source.example.com", Port = 8080 };
        var entry = new WatchHistoryEntry { XtreamSource = source, ContentType = ContentType.Vod, TmdbId = 603, StartedAtUtc = DateTimeOffset.UtcNow };
        _dbContext.WatchHistory.Add(entry);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        var result = await SourceEndpoints.DeleteAsync(source.Id, _sourceAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        var kept = await _dbContext.WatchHistory.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal((entry.Id, (int?)null), (kept.Id, kept.XtreamSourceId));
    }

    [Fact]
    public async Task DeleteAsync_WhenCategoriesAreMappedToCustomCategory_DeletesXtreamCategoriesAndKeepsCustomCategory()
    {
        var customCategory = new CustomCategory { Name = "Movies", ContentType = ContentType.Vod };
        var source = new XtreamSource
        {
            Protocol = "http",
            Host = "source.example.com",
            Port = 8080,
            XtreamCategories = [new XtreamCategory { XtreamId = "1", Name = "Action", ContentType = ContentType.Vod, CustomCategory = customCategory }]
        };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await SourceEndpoints.DeleteAsync(source.Id, _sourceAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        Assert.False(await _dbContext.XtreamCategories.AnyAsync(TestContext.Current.CancellationToken));
        Assert.True(await _dbContext.CustomCategories.AnyAsync(c => c.Id == customCategory.Id, TestContext.Current.CancellationToken));
    }

    private Task<IResult> PostAsync(IHttpClientFactory httpClientFactory, XtreamSourceCreateRequest request)
    {
        var discoveryService = new XtreamCategoryDiscoveryAdminService(httpClientFactory, new CategoryService(_dbContext, TimeProvider.System), NullLogger<XtreamCategoryDiscoveryAdminService>.Instance);
        var validator = new XtreamProviderValidator(Microsoft.Extensions.Options.Options.Create(new XtreamProxyOptions { AllowedHosts = ["provider.example.com"] }));

        return SourceEndpoints.PostAsync(request, _sourceAdminService, discoveryService, validator, TestContext.Current.CancellationToken);
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
