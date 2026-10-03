using Microsoft.AspNetCore.Http.HttpResults;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class SourcesGetEndpointTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SourcesGetEndpoint _endpoint;

    public SourcesGetEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();

        _endpoint = new SourcesGetEndpoint(new SourceAdminService(_dbContext));
    }

    [Fact]
    public async Task GetAsync_WhenSourcesExist_ReturnsOkWithExpectedSources()
    {
        _dbContext.XtreamSources.AddRange(
            new XtreamSource { Protocol = "http", Host = "source1.example.com", Port = 8080 },
            new XtreamSource { Protocol = "https", Host = "source2.example.com", Port = 443 });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _endpoint.GetAsync(TestContext.Current.CancellationToken);

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
        var result = await _endpoint.GetAsync(TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<XtreamSourceSummaryDto>>>(result);
        Assert.NotNull(ok.Value);
        var dtos = ok.Value.ToList();
        Assert.Empty(dtos);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }
}
