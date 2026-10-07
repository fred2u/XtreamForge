using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using XtreamForge.ApiService.Endpoints.Admin;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.Database;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints;

/// <summary>
/// The admin handlers receive their services as parameters: these tests map them with the services of the application
/// to check that every parameter binds from the expected source (services, route, query, body).
/// </summary>
public sealed class AdminRoutesTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext = SqliteDbContextFactory.Create();

    private WebApplication? _app;

    [Fact]
    public async Task MapAdminEndpoints_BuildsEveryRoute()
    {
        await StartAsync();

        var routes = ((IEndpointRouteBuilder)GetApp()).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        Assert.Equal(36, routes.Count);
        Assert.All(routes, route => Assert.StartsWith("/api/admin/", route.RoutePattern.RawText, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/admin/status", HttpStatusCode.OK)]
    [InlineData("/api/admin/queues", HttpStatusCode.OK)]
    [InlineData("/api/admin/sources", HttpStatusCode.OK)]
    [InlineData("/api/admin/sources/999/category-rules?contentType=Vod", HttpStatusCode.NotFound)]
    [InlineData("/api/admin/tmdb-rules?contentType=Vod", HttpStatusCode.OK)]
    [InlineData("/api/admin/tmdb-infos?contentType=Vod&skip=0&take=10", HttpStatusCode.OK)]
    [InlineData("/api/admin/tmdb-infos?contentType=Undefined", HttpStatusCode.BadRequest)]
    [InlineData("/api/admin/sources/999/tmdb-mappings?contentType=Vod", HttpStatusCode.NotFound)]
    [InlineData("/api/admin/watch-history?skip=0&take=10", HttpStatusCode.OK)]
    [InlineData("/api/admin/watch-history?contentType=Series&skip=0&take=10", HttpStatusCode.OK)]
    [InlineData("/api/admin/watch-history/activity?timeZone=UTC", HttpStatusCode.OK)]
    [InlineData("/api/admin/recommendations", HttpStatusCode.OK)]
    [InlineData("/api/admin/recommendations?contentType=Series", HttpStatusCode.OK)]
    [InlineData("/api/admin/recommendations?contentType=Undefined", HttpStatusCode.BadRequest)]
    public async Task Get_BindsRouteQueryAndServices(string path, HttpStatusCode expectedStatusCode)
    {
        var client = await StartAsync();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(expectedStatusCode, response.StatusCode);
    }

    [Fact]
    public async Task PostCustomCategory_BindsTheBodyThenListsTheCategory()
    {
        var client = await StartAsync();

        using var created = await client.PostAsJsonAsync("/api/admin/custom-categories", new { name = "Movies", contentType = 1 }, TestContext.Current.CancellationToken);
        var categories = await client.GetStringAsync("/api/admin/custom-categories?contentType=Vod", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("\"name\":\"Movies\"", categories, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutTmdbRuleOrder_BindsTheBody()
    {
        var client = await StartAsync();

        using var response = await client.PutAsJsonAsync("/api/admin/tmdb-rules/order", new { contentType = 1, ruleIds = Array.Empty<int>() }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostWatchHistory_BindsTheTmdbInfoIdFromTheRoute()
    {
        var client = await StartAsync();

        using var response = await client.PostAsync("/api/admin/tmdb-infos/42/watch-history", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("category-rules")]
    [InlineData("item-rules")]
    public async Task PostSourceRule_CreatesTheRuleAtTheLocationOfTheRequestPath(string segment)
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        var client = await StartAsync();
        var path = $"/api/admin/sources/{source.Id}/{segment}";

        using var response = await client.PostAsJsonAsync(
            path,
            new { contentType = 1, sequence = 10, action = 2, @operator = 2, pattern = "Kids", caseSensitive = false, isEnabled = true, field = (int?)null },
            TestContext.Current.CancellationToken);
        var rules = await client.GetStringAsync($"{path}?contentType=Vod", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Matches($"^{path}/[0-9]+$", response.Headers.Location?.OriginalString);
        Assert.Contains("\"pattern\":\"Kids\"", rules, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/admin/sources/1/category-rules")]
    [InlineData("/api/admin/sources/1/item-rules")]
    [InlineData("/api/admin/tmdb-rules")]
    public async Task PostRule_WithoutPattern_ReturnsBadRequest(string path)
    {
        var client = await StartAsync();

        using var response = await client.PostAsJsonAsync(
            path,
            new { contentType = 1, sequence = 10, action = 2, @operator = 2, pattern = (string?)null, caseSensitive = false, isEnabled = true, field = 2 },
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpClient> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tmdb:BaseUrl"] = "https://api.themoviedb.org/3/",
            ["XtreamProxy:AllowedHosts:0"] = "provider.example.com",
            ["Recommendations:CategoryId"] = "999999999",
            ["Popular:CategoryId"] = "999999998"
        });

        builder.Services.AddBindedOptions();
        builder.Services.AddHttpClients();
        builder.Services.AddServices();
        builder.Services.AddScoped(_ => SqliteDbContextFactory.CreateOnSameDatabase(_dbContext));

        _app = builder.Build();
        _app.MapAdminEndpoints();
        await _app.StartAsync(TestContext.Current.CancellationToken);

        return _app.GetTestClient();
    }

    private WebApplication GetApp() => _app ?? throw new InvalidOperationException("The application is not started.");

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();

        await _dbContext.DisposeAsync();
    }
}
