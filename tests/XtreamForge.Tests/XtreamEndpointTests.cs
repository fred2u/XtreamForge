using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XtreamForge.Data;
using XtreamForge.Categories;

namespace XtreamForge.Tests;

public sealed class XtreamEndpointTests : IClassFixture<XtreamForgeApiFactory>
{
    private readonly XtreamForgeApiFactory _factory;

    public XtreamEndpointTests(XtreamForgeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStatus_ReturnsExpectedPayload()
    {
        using var client = _factory.CreateClient();

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

    [Theory]
    [InlineData("http", "/http/example.com/8080/player_api.php?action=get_live_categories")]
    [InlineData("https", "/https/example.com/443/player_api.php?action=get_live_categories")]
    public async Task SupportedProtocols_AreAccepted(string expectedScheme, string requestPath)
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(requestPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedScheme, Assert.Single(handler.Requests).RequestUri?.Scheme);
    }

    [Fact]
    public async Task InvalidProtocol_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/ftp/example.com/80/player_api.php");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("65536")]
    public async Task InvalidPort_ReturnsBadRequest(string port)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/http/example.com/{port}/player_api.php");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DisallowedHost_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/http/not-allowed.example/8080/player_api.php");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetVodStreams_WithOutputCategoryFilter_UsesEffectiveReverseMappings_AndRewritesCategoryIds()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Vod,
                [
                    new DiscoveredCategory("10", "Movies A"),
                    new DiscoveredCategory("20", "SPORT Movies"),
                    new DiscoveredCategory("30", "Movies B")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category10 = categories.Single(category => category.UpstreamCategoryId == "10");
            var category20 = categories.Single(category => category.UpstreamCategoryId == "20");
            var category30 = categories.Single(category => category.UpstreamCategoryId == "30");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category20.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies"));
            var customCategoryRecordId = await dbContext.CustomCategories.Select(category => category.Id).SingleAsync();
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, customCategoryRecordId, null));
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = new FakeForwarderHandler((request, _) =>
        {
            var action = ParseQuery(request.RequestUri, "action");
            var categoryId = ParseQuery(request.RequestUri, "category_id");

            return Task.FromResult(action switch
            {
                "get_vod_streams" when categoryId == "10" => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "[{\"stream_id\":\"100\",\"name\":\"Movie A\",\"category_id\":\"10\"}]"),
                "get_vod_streams" when categoryId == "30" => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "[{\"stream_id\":\"300\",\"name\":\"Movie B\",\"category_id\":\"30\"}]"),
                _ => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.BadRequest, "{}")
            });
        });

        await using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var customCategoryId = await verifyDbContext.CustomCategories.Select(category => category.XtreamForgeCategoryId).SingleAsync();

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/https/example.com/443/player_api.php?action=get_vod_streams&category_id={customCategoryId}");
        var payload = await response.Content.ReadFromJsonAsync<List<StreamResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Single(payload);
        Assert.Equal(("300", customCategoryId.ToString()), (payload[0].Id, payload[0].CategoryId));
        Assert.Single(handler.Requests);
        Assert.Contains(handler.Requests, request => request.RequestUri?.Query.Contains("category_id=30", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri?.Query.Contains("category_id=20", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri?.Query.Contains("category_id=10", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("&category_id=ALL", "ALL")]
    [InlineData("&category_id=all", "all")]
    [InlineData("&category_id=All", "All")]
    [InlineData("&category_id=", "")]
    public async Task GetVodStreams_AllCategoryModes_UseSingleUpstreamRequest_AndApplyEquivalentTransformations(string querySuffix, string? expectedForwardedCategoryId)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Vod,
                [
                    new DiscoveredCategory("10", "Movies A"),
                    new DiscoveredCategory("20", "SPORT Movies"),
                    new DiscoveredCategory("30", "Movies B")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category20 = categories.Single(category => category.UpstreamCategoryId == "20");
            var category30 = categories.Single(category => category.UpstreamCategoryId == "30");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category20.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Disabled, null, null));
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies B Shared"));
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = new FakeForwarderHandler((request, _) =>
        {
            var action = ParseQuery(request.RequestUri, "action");
            return Task.FromResult(action switch
            {
                "get_vod_streams" => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, """
                    [
                      {"stream_id":"101","name":"Movie A","category_id":"10","category_ids":["10","20"],"stream_icon":"poster-a"},
                      {"stream_id":"102","name":"Filtered","category_id":"20","category_ids":["20"],"stream_icon":"poster-b"},
                      {"stream_id":"103","name":"Movie B","category_id":"30","category_ids":"[\"30\"]","stream_icon":"poster-c"},
                      {"stream_id":"103","name":"Movie B duplicate","category_id":"30","category_ids":["30"],"stream_icon":"poster-c-dup"}
                    ]
                    """),
                _ => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.BadRequest, "{}")
            });
        });

        using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var customCategoryId = await verifyDbContext.CustomCategories.Select(category => category.XtreamForgeCategoryId).SingleAsync();

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/https/example.com/443/player_api.php?action=get_vod_streams{querySuffix}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);
        Assert.Equal(expectedForwardedCategoryId, ParseQuery(Assert.Single(handler.Requests).RequestUri, "category_id"));

        var payload = await response.Content.ReadFromJsonAsync<List<ExtendedStreamResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Count);

        var movieA = Assert.Single(payload, item => item.Id == "101");
        Assert.Equal("1", movieA.CategoryId);
        Assert.Equal(["1"], movieA.CategoryIds);
        Assert.Equal("poster-a", movieA.StreamIcon);

        var movieB = Assert.Single(payload, item => item.Id == "103");
        Assert.Equal(customCategoryId.ToString(), movieB.CategoryId);
        Assert.Equal([customCategoryId.ToString()], movieB.CategoryIds);
        Assert.Equal("poster-c", movieB.StreamIcon);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("&category_id=ALL", "ALL")]
    public async Task GetSeries_AllCategoryModes_UseSingleUpstreamRequest_AndApplyEffectiveMappings(string querySuffix, string? expectedForwardedCategoryId)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Series,
                [
                    new DiscoveredCategory("10", "Drama"),
                    new DiscoveredCategory("20", "SPORT Series"),
                    new DiscoveredCategory("30", "Drama Plus")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            var category30 = await dbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "30");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Series, CategoryMappingSelection.Custom, null, "Drama Shared"));
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Series, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = new FakeForwarderHandler((request, _) =>
        {
            var action = ParseQuery(request.RequestUri, "action");
            return Task.FromResult(action switch
            {
                "get_series" => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, """
                    [
                      {"series_id":"501","name":"Series A","category_id":"10"},
                      {"series_id":"502","name":"Series B","category_id":"20"},
                      {"series_id":"503","name":"Series C","category_id":"30","category_ids":["30"]},
                      {"series_id":"503","name":"Series C duplicate","category_id":"30","category_ids":["30"]}
                    ]
                    """),
                _ => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.BadRequest, "{}")
            });
        });

        using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var customCategoryId = await verifyDbContext.CustomCategories.Select(category => category.XtreamForgeCategoryId).SingleAsync();

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        var response = await client.GetAsync($"/https/example.com/443/player_api.php?action=get_series{querySuffix}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);
        Assert.Equal(expectedForwardedCategoryId, ParseQuery(Assert.Single(handler.Requests).RequestUri, "category_id"));

        var payload = await response.Content.ReadFromJsonAsync<List<ExtendedSeriesResponse>>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.Count);
        Assert.Equal(("501", "1"), (payload[0].Id, payload[0].CategoryId));
        Assert.Equal(("503", customCategoryId.ToString()), (payload[1].Id, payload[1].CategoryId));
        Assert.NotNull(payload[1].CategoryIds);
        Assert.Equal([customCategoryId.ToString()], payload[1].CategoryIds!);
    }

    [Fact]
    public async Task GetVodStreams_MissingCategoryId_AndExplicitAll_ProduceEquivalentTransformedJson()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Vod,
                [
                    new DiscoveredCategory("10", "Movies A"),
                    new DiscoveredCategory("20", "SPORT Movies")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = CreateJsonHandler("""
            [
              {"stream_id":"101","name":"Movie A","category_id":"10","category_ids":["10","20"]},
              {"stream_id":"102","name":"Filtered","category_id":"20","category_ids":["20"]}
            ]
            """);

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var missingResponse = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_streams");
        var allResponse = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_streams&category_id=ALL");

        var missingJson = JsonNode.Parse(await missingResponse.Content.ReadAsStringAsync());
        var allJson = JsonNode.Parse(await allResponse.Content.ReadAsStringAsync());

        Assert.True(JsonNode.DeepEquals(missingJson, allJson));
    }

    [Fact]
    public async Task GetSeries_MissingCategoryId_AndExplicitAll_ProduceEquivalentTransformedJson()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Series,
                [
                    new DiscoveredCategory("10", "Drama"),
                    new DiscoveredCategory("20", "SPORT Series")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Series, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = CreateJsonHandler("""
            [
              {"series_id":"501","name":"Series A","category_id":"10","category_ids":["10"]},
              {"series_id":"502","name":"Filtered","category_id":"20","category_ids":["20"]}
            ]
            """);

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var missingResponse = await client.GetAsync("/https/example.com/443/player_api.php?action=get_series");
        var allResponse = await client.GetAsync("/https/example.com/443/player_api.php?action=get_series&category_id=ALL");

        var missingJson = JsonNode.Parse(await missingResponse.Content.ReadAsStringAsync());
        var allJson = JsonNode.Parse(await allResponse.Content.ReadAsStringAsync());

        Assert.True(JsonNode.DeepEquals(missingJson, allJson));
    }

    [Fact]
    public async Task GetVodInfo_RewritesCategoryReferences_ToXtreamForgeIds()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Vod,
                [
                    new DiscoveredCategory("10", "Movies A"),
                    new DiscoveredCategory("20", "SPORT Movies"),
                    new DiscoveredCategory("30", "Movies B")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category30 = categories.Single(category => category.UpstreamCategoryId == "30");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies"));
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = new FakeForwarderHandler((request, _) =>
        {
            var action = ParseQuery(request.RequestUri, "action");
            return Task.FromResult(action switch
            {
                "get_vod_info" => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"info\":{\"category_id\":\"10\",\"category_ids\":[\"10\",\"20\",\"30\"]},\"movie_data\":{\"category_id\":\"30\"}}"),
                _ => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.BadRequest, "{}")
            });
        });

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_info&vod_id=100");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        await using var verifyScope = setupFactory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var customCategoryId = await verifyDbContext.CustomCategories.Select(category => category.XtreamForgeCategoryId).SingleAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", payload.GetProperty("info").GetProperty("category_id").GetString());
        Assert.Equal(["1", customCategoryId.ToString()], payload.GetProperty("info").GetProperty("category_ids").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(customCategoryId.ToString(), payload.GetProperty("movie_data").GetProperty("category_id").GetString());
    }

    [Fact]
    public async Task GetSeriesInfo_WhenAllCategoriesAreExcluded_ReturnsNotFound()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();

            await mappingService.SyncCategoriesAsync(
                new XtreamSourceDescriptor("https", "example.com", 443),
                ContentType.Series,
                [
                    new DiscoveredCategory("20", "SPORT Series")
                ]);

            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Series, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        var handler = new FakeForwarderHandler((request, _) =>
        {
            var action = ParseQuery(request.RequestUri, "action");
            return Task.FromResult(action switch
            {
                "get_series_info" => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"info\":{\"category_id\":\"20\",\"category_ids\":[\"20\"]}}"),
                _ => FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.BadRequest, "{}")
            });
        });

        using var factory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_series_info&series_id=500");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(ContentType.Vod, "get_vod_categories")]
    [InlineData(ContentType.Series, "get_series_categories")]
    public async Task CategoryActions_AreRewritten_AndDiscoveredInDatabase(ContentType contentType, string action)
    {
        var upstreamPayload = "[{\"category_id\":\"42\",\"category_name\":\"|FR| 4K ⁴ᴷ\"},{\"category_id\":\"57\",\"category_name\":\"|FR| FILMS 4K UHD\"}]";
        var handler = CreateJsonHandler(upstreamPayload);
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");

        using var factory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(factory);
        using var scopedFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = scopedFactory.CreateClient();

        var response = await client.GetAsync($"/https/example.com/443/player_api.php?action={action}");
        var payload = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Single(handler.Requests);
        Assert.Collection(
            payload,
            first =>
            {
                Assert.Equal("1", first.CategoryId);
                Assert.Equal("|FR| 4K ⁴ᴷ", first.CategoryName);
            },
            second =>
            {
                Assert.Equal("2", second.CategoryId);
                Assert.Equal("|FR| FILMS 4K UHD", second.CategoryName);
            });

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var source = await dbContext.XtreamSources.SingleAsync();
        var upstreamCategories = await dbContext.UpstreamCategories.Where(category => category.ContentType == contentType).OrderBy(category => category.UpstreamCategoryId).ToListAsync();
        var outputCategories = await dbContext.OutputCategories.Where(category => category.ContentType == contentType).OrderBy(category => category.XtreamForgeCategoryId).ToListAsync();

        Assert.Equal("https", source.Protocol);
        Assert.Equal("example.com", source.Host);
        Assert.Equal(443, source.Port);
        Assert.Equal(2, upstreamCategories.Count);
        Assert.Equal(2, outputCategories.Count);
        Assert.All(upstreamCategories, category => Assert.False(category.IsExcluded));
    }

    [Fact]
    public async Task CategoryDiscovery_FromClientRequest_DoesNotPersistCredentials_AndDoesNotRefetchCategories()
    {
        var handler = CreateJsonHandler("[{\"category_id\":\"42\",\"category_name\":\"Alpha\"}]");
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");

        using var factory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(factory);
        using var scopedFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(handler);
        using var client = scopedFactory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories&username=test-user&******");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(handler.Requests);

        await using var scope = scopedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var source = await dbContext.XtreamSources.SingleAsync();
        var category = await dbContext.UpstreamCategories.SingleAsync();

        Assert.Equal("https", source.Protocol);
        Assert.Equal("example.com", source.Host);
        Assert.Equal(443, source.Port);
        Assert.DoesNotContain("test-user", source.Protocol, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-user", source.Host, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-password", category.UpstreamCategoryName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-password", category.UpstreamCategoryId, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CategoryConfiguration_CanRenameMergeAndExcludeCategories()
    {
        var upstreamPayload = "[{\"category_id\":\"42\",\"category_name\":\"|FR| 4K ⁴ᴷ\"},{\"category_id\":\"57\",\"category_name\":\"|FR| FILMS 4K UHD\"},{\"category_id\":\"94\",\"category_name\":\"|xxx| Something\"}]";
        var initialHandler = CreateJsonHandler(upstreamPayload);
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");

        using var factory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(factory);

        using (var discoveryFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(initialHandler))
        using (var discoveryClient = discoveryFactory.CreateClient())
        {
            var discoveryResponse = await discoveryClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
            Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category42 = categories.Single(category => category.UpstreamCategoryId == "42");
            var category57 = categories.Single(category => category.UpstreamCategoryId == "57");
            var category94 = categories.Single(category => category.UpstreamCategoryId == "94");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                category42.Id,
                category42.XtreamSourceId,
                ContentType.Vod,
                CategoryMappingSelection.Custom,
                null,
                "|FR| FILMS 4K"));

            var customCategoryId = await dbContext.CustomCategories.Select(category => category.Id).SingleAsync();
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                category57.Id,
                category57.XtreamSourceId,
                ContentType.Vod,
                CategoryMappingSelection.Custom,
                customCategoryId,
                null));

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(
                category94.Id,
                category94.XtreamSourceId,
                ContentType.Vod,
                CategoryMappingSelection.Disabled,
                null,
                null));
        }

        var rewriteHandler = CreateJsonHandler(upstreamPayload);
        using var rewriteFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(rewriteHandler);
        using var client = rewriteFactory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var payload = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Single(payload);
        await using var verifyScope = rewriteFactory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        var customCategory = await verifyDbContext.CustomCategories.SingleAsync();
        Assert.Equal(customCategory.XtreamForgeCategoryId.ToString(), payload[0].CategoryId);
        Assert.Equal("|FR| FILMS 4K", payload[0].CategoryName);

        var excludedCategory = await verifyDbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "94");
        var mergedCategory = await verifyDbContext.UpstreamCategories.SingleAsync(category => category.UpstreamCategoryId == "57");
        Assert.True(excludedCategory.IsExcluded);
        Assert.Equal(customCategory.Id, mergedCategory.CustomCategoryId);
    }

    [Fact]
    public async Task CategoryRefresh_KeepsStableXtreamForgeIdsAcrossReorderingAndAdditions()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var factory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(factory);

        using (var firstFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler("[{\"category_id\":\"42\",\"category_name\":\"Alpha\"},{\"category_id\":\"57\",\"category_name\":\"Beta\"}]")))
        using (var firstClient = firstFactory.CreateClient())
        {
            await firstClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        }

        using var secondFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler("[{\"category_id\":\"57\",\"category_name\":\"Beta\"},{\"category_id\":\"81\",\"category_name\":\"Gamma\"},{\"category_id\":\"42\",\"category_name\":\"Alpha\"}]"));
        using var secondClient = secondFactory.CreateClient();

        var response = await secondClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var payload = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Collection(
            payload,
            first => Assert.Equal(("1", "Alpha"), (first.CategoryId, first.CategoryName)),
            second => Assert.Equal(("2", "Beta"), (second.CategoryId, second.CategoryName)),
            third => Assert.Equal(("3", "Gamma"), (third.CategoryId, third.CategoryName)));
    }

    [Fact]
    public async Task CategoryRules_ExcludeCategoriesFromVodOutput_WithoutDeletingDiscovery()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        using (var discoveryFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler("[{\"category_id\":\"10\",\"category_name\":\"Movies\"},{\"category_id\":\"20\",\"category_name\":\"SPORT\"}]")))
        using (var discoveryClient = discoveryFactory.CreateClient())
        {
            var discoveryResponse = await discoveryClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
            Assert.Equal(HttpStatusCode.OK, discoveryResponse.StatusCode);
        }

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();
            var sourceId = await scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>().XtreamSources.Select(source => source.Id).SingleAsync();
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        using var rewriteFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler("[{\"category_id\":\"10\",\"category_name\":\"Movies\"},{\"category_id\":\"20\",\"category_name\":\"SPORT\"}]"));
        using var client = rewriteFactory.CreateClient();
        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var payload = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Single(payload);
        Assert.Equal("Movies", payload[0].CategoryName);

        await using var verifyScope = rewriteFactory.Services.CreateAsyncScope();
        var verifyDbContext = verifyScope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        Assert.Equal(2, await verifyDbContext.UpstreamCategories.CountAsync());
    }

    [Fact]
    public async Task CategoryRules_SeriesIsolation_AppliesOnlyToSeriesCategories()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);

        using (var discoveryFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler("[{\"category_id\":\"10\",\"category_name\":\"SPORT\"}]")))
        using (var discoveryClient = discoveryFactory.CreateClient())
        {
            await discoveryClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
            await discoveryClient.GetAsync("/https/example.com/443/player_api.php?action=get_series_categories");
        }

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();
            var sourceId = await scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>().XtreamSources.Select(source => source.Id).SingleAsync();
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Series, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        using var rewriteFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler("[{\"category_id\":\"10\",\"category_name\":\"SPORT\"}]"));
        using var client = rewriteFactory.CreateClient();

        var vodResponse = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var seriesResponse = await client.GetAsync("/https/example.com/443/player_api.php?action=get_series_categories");

        Assert.Single((await vodResponse.Content.ReadFromJsonAsync<List<CategoryResponse>>())!);
        Assert.Empty((await seriesResponse.Content.ReadFromJsonAsync<List<CategoryResponse>>())!);
    }

    [Fact]
    public async Task CategoryRules_KeepMergedOutputVisible_WhenAnotherMappedCategoryRemainsIncluded()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"xtreamforge-api-tests-{Guid.NewGuid():N}.db");
        using var setupFactory = _factory.WithSqliteDatabase(databasePath);
        await EnsureDatabaseCreatedAsync(setupFactory);
        const string upstreamPayload = "[{\"category_id\":\"10\",\"category_name\":\"Movies A\"},{\"category_id\":\"20\",\"category_name\":\"SPORT Movies\"},{\"category_id\":\"30\",\"category_name\":\"Movies B\"}]";

        using (var discoveryFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler(upstreamPayload)))
        using (var discoveryClient = discoveryFactory.CreateClient())
        {
            await discoveryClient.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        }

        await using (var scope = setupFactory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
            var mappingService = scope.ServiceProvider.GetRequiredService<XtreamCategoryMappingService>();
            var ruleService = scope.ServiceProvider.GetRequiredService<CategoryRuleService>();
            var sourceId = await dbContext.XtreamSources.Select(source => source.Id).SingleAsync();
            var categories = await dbContext.UpstreamCategories.OrderBy(category => category.UpstreamCategoryId).ToListAsync();
            var category20 = categories.Single(category => category.UpstreamCategoryId == "20");
            var category30 = categories.Single(category => category.UpstreamCategoryId == "30");

            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category20.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, null, "Movies"));
            var customCategoryId = await dbContext.CustomCategories.Select(category => category.Id).SingleAsync();
            await mappingService.SaveCategoryConfigurationAsync(new CategoryConfigurationCommand(category30.Id, sourceId, ContentType.Vod, CategoryMappingSelection.Custom, customCategoryId, null));
            await ruleService.CreateRuleAsync(new CategoryRuleEditorCommand(null, sourceId, ContentType.Vod, CategoryRuleAction.Exclude, CategoryRuleOperator.Contains, "SPORT", false, true));
        }

        using var rewriteFactory = _factory.WithSqliteDatabase(databasePath).WithForwarderHandler(CreateJsonHandler(upstreamPayload));
        using var client = rewriteFactory.CreateClient();
        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories");
        var payload = await response.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.NotNull(payload);
        Assert.Equal(2, payload.Count);
        Assert.Contains(payload, category => category.CategoryName == "Movies A");
        Assert.Contains(payload, category => category.CategoryName == "Movies");
    }

    [Fact]
    public async Task UnknownPlayerApiAction_IsForwarded()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/http/example.com/8080/player_api.php?action=get_live_categories&category_id=12");

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("?action=get_live_categories&category_id=12", forwardedRequest.RequestUri?.Query);
    }

    [Fact]
    public async Task PlayerApiWithoutAction_IsForwarded()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?username=user&******");

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/player_api.php", forwardedRequest.RequestUri?.AbsolutePath);
        Assert.Equal("?username=user&******", forwardedRequest.RequestUri?.Query);
    }

    [Theory]
    [InlineData("/https/example.com/443?ping=true", "/", "?ping=true")]
    [InlineData("/http/example.com/8080", "/", "")]
    [InlineData("/http/example.com/8080/xmltv.php?username=user&******", "/xmltv.php", "?username=user&******")]
    public async Task NonPlayerApiRequests_AreForwarded(string requestPath, string expectedPath, string expectedQuery)
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(requestPath);

        var forwardedRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedPath, forwardedRequest.RequestUri?.AbsolutePath);
        Assert.Equal(expectedQuery, forwardedRequest.RequestUri?.Query);
    }

    [Fact]
    public async Task Forwarding_PreservesMethodHeadersAndRequestBody_ButNotHostOrHopByHopHeaders()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/https/example.com/443/player_api.php?action=get_live_categories")
        {
            Content = new StringContent("sample-body")
        };
        request.Headers.Host = "client.example.test";
        request.Headers.Add("X-Test-Header", "header-value");
        request.Headers.Connection.Add("X-Transient");
        request.Headers.Add("X-Transient", "remove-me");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        var response = await client.SendAsync(request);
        var forwardedRequest = Assert.Single(handler.Requests);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("POST", forwardedRequest.Method);
        Assert.Equal("sample-body", forwardedRequest.Body);
        Assert.True(forwardedRequest.Headers.TryGetValue("X-Test-Header", out var headerValues));
        Assert.Equal(["header-value"], headerValues);
        Assert.False(forwardedRequest.Headers.ContainsKey("Connection"));
        Assert.False(forwardedRequest.Headers.ContainsKey("X-Transient"));
        Assert.Null(forwardedRequest.Host);
    }

    [Fact]
    public async Task Forwarding_PreservesExplicitEmptyRequestBodyContentHeaders()
    {
        var handler = CreateForwardingHandler();
        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/http/example.com/8080/player_api.php?action=get_live_categories")
        {
            Content = new ByteArrayContent([])
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await client.SendAsync(request);
        var forwardedRequest = Assert.Single(handler.Requests);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, forwardedRequest.Body);
        Assert.True(forwardedRequest.Headers.TryGetValue("Content-Type", out var contentTypeValues));
        Assert.Equal(["application/json"], contentTypeValues);
    }

    [Fact]
    public async Task Forwarding_PropagatesStatusHeadersAndBody_AndRemovesHopByHopHeaders()
    {
        var handler = new FakeForwarderHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("upstream-body")
            };
            response.Headers.Add("X-Upstream-Header", "propagated");
            response.Headers.Connection.Add("X-Hop-Response");
            response.Headers.TransferEncodingChunked = true;
            response.Headers.Add("X-Hop-Response", "remove-me");
            response.Content.Headers.Add("X-Upstream-Content-Header", "content-value");
            return Task.FromResult(response);
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_live_categories");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("propagated", Assert.Single(response.Headers.GetValues("X-Upstream-Header")));
        Assert.Equal("content-value", Assert.Single(GetHeaderValues(response, "X-Upstream-Content-Header")));
        Assert.False(response.Headers.Contains("Connection"));
        Assert.False(response.Headers.Contains("X-Hop-Response"));
        Assert.False(response.Headers.TransferEncodingChunked.HasValue && response.Headers.TransferEncodingChunked.Value);
        Assert.Equal("upstream-body", content);
    }

    [Fact]
    public async Task HeadRequests_DoNotCopyAResponseBody()
    {
        var handler = new FakeForwarderHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent("ignored-body")
            });
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Head, "/https/example.com/443/player_api.php?action=get_live_categories");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(body);
    }

    [Fact]
    public async Task RequestCancellation_IsPropagatedThroughRequestAborted()
    {
        var handler = new FakeForwarderHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}");
        });

        using var factory = _factory.WithForwarderHandler(handler);
        using var client = factory.CreateClient();
        using var cancellationTokenSource = new CancellationTokenSource();

        var requestTask = client.GetAsync(
            "/https/example.com/443/player_api.php?action=get_live_categories",
            cancellationTokenSource.Token);

        await handler.RequestReceived.Task;
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await requestTask);
        Assert.True(handler.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task UpstreamConnectionFailure_ReturnsBadGateway_WithoutLoggingCredentials()
    {
        var logSink = new TestLogSink();
        var handler = new FakeForwarderHandler((_, _) => throw new HttpRequestException("Connection refused"));

        using var factory = _factory.WithForwarderHandler(handler, logSink);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?username=test-user&******");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains(logSink.Messages, message => message.Contains("example.com:443", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("test-password", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("username=test-user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpstreamTimeout_ReturnsGatewayTimeout_WithoutLoggingCredentials()
    {
        var logSink = new TestLogSink();
        var handler = new FakeForwarderHandler((_, _) =>
            throw new TaskCanceledException("Timed out", new TimeoutException("upstream timeout")));

        using var factory = _factory.WithForwarderHandler(handler, logSink);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/http/example.com/8080/player_api.php?username=user&******");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains(logSink.Messages, message => message.Contains("example.com:8080", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("very-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("series_id=42", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidCategoryJson_ReturnsBadGateway_WithoutLoggingCredentials()
    {
        var logSink = new TestLogSink();
        var handler = CreateJsonHandler("{not-json");
        using var factory = _factory.WithForwarderHandler(handler, logSink, failIfDatabaseAccessed: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/https/example.com/443/player_api.php?action=get_vod_categories&username=user&******");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains(logSink.Messages, message => message.Contains("invalid category payload", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("hidden-secret", StringComparison.Ordinal));
        Assert.DoesNotContain(logSink.Messages, message => message.Contains("username=user", StringComparison.Ordinal));
    }

    private static FakeForwarderHandler CreateForwardingHandler() =>
        new((_, _) => Task.FromResult(FakeForwarderHandler.CreateJsonResponse(HttpStatusCode.OK, "{\"forwarded\":true}")));

    private static FakeForwarderHandler CreateJsonHandler(string json) =>
        new((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        });

    private static async Task EnsureDatabaseCreatedAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<XtreamForgeDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    private static IEnumerable<string> GetHeaderValues(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var responseHeaderValues))
        {
            return responseHeaderValues;
        }

        if (response.Content.Headers.TryGetValues(headerName, out var contentHeaderValues))
        {
            return contentHeaderValues;
        }

        throw new InvalidOperationException($"The given header was not found: {headerName}.");
    }

    private static string? ParseQuery(Uri? requestUri, string key)
    {
        if (requestUri is null)
        {
            return null;
        }

        var match = Regex.Match(requestUri.Query, $@"(?:\?|&){Regex.Escape(key)}=([^&]*)");
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }

    private sealed record StatusResponse(string ApplicationName, string ApplicationVersion, string Status);

    private sealed record CategoryResponse(
        [property: JsonPropertyName("category_id")] string CategoryId,
        [property: JsonPropertyName("category_name")] string CategoryName);

    private sealed record StreamResponse(
        [property: JsonPropertyName("stream_id")] string Id,
        [property: JsonPropertyName("category_id")] string CategoryId);

    private sealed record ExtendedStreamResponse(
        [property: JsonPropertyName("stream_id")] string Id,
        [property: JsonPropertyName("category_id")] string CategoryId,
        [property: JsonPropertyName("category_ids")] string[] CategoryIds,
        [property: JsonPropertyName("stream_icon")] string StreamIcon);

    private sealed record SeriesResponse(
        [property: JsonPropertyName("series_id")] string Id,
        [property: JsonPropertyName("category_id")] string CategoryId);

    private sealed record ExtendedSeriesResponse(
        [property: JsonPropertyName("series_id")] string Id,
        [property: JsonPropertyName("category_id")] string CategoryId,
        [property: JsonPropertyName("category_ids")] string[]? CategoryIds);
}
