using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Infrastructure.Data;

namespace XtreamForge.Api.Tests;

public sealed class StatusEndpointTests : IClassFixture<XtreamForgeApiFactory>
{
    private readonly XtreamForgeApiFactory _factory;

    public StatusEndpointTests(XtreamForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStatus_ReturnsExpectedPayload()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/status");
        var payload = await response.Content.ReadFromJsonAsync<StatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("XtreamForge", payload.ApplicationName);
        Assert.Equal("Healthy", payload.Status);
        Assert.False(string.IsNullOrWhiteSpace(payload.ApplicationVersion));
    }

    [Fact]
    public void DependencyInjection_CanConstructInfrastructureServices()
    {
        using var scope = _factory.Services.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();

        Assert.NotNull(dbContext);
    }

    private sealed record StatusResponse(string ApplicationName, string ApplicationVersion, string Status);
}
