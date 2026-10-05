using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Endpoints.Admin.Rules;
using XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings.Dto;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Domain.Tmdb;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class TmdbEndpointsTests : IAsyncDisposable
{
    private static readonly IOptions<TmdbOptions> TmdbOptions = Options.Create(new TmdbOptions { ImageBaseUrl = "https://image.tmdb.org/t/p/" });

    private readonly XtreamForgeDbContext _dbContext;

    public TmdbEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
    }

    // ─── TMDB rules ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PostRule_WhenValid_ReturnsCreatedRule()
    {
        var result = await TmdbRuleEndpoints.PostAsync(Request(ContentType.Vod, "Horror"), new TmdbRuleAdminService(_dbContext), TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<AdminRuleDto>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal($"/api/admin/tmdb-rules/{created.Value.Id}", created.Location);
        Assert.Equal(
            ((int?)null, ContentType.Vod, (TmdbRuleField?)TmdbRuleField.Genre, "Horror"),
            (created.Value.XtreamSourceId, created.Value.ContentType, created.Value.Field, created.Value.Pattern));
    }

    [Theory]
    [InlineData(ContentType.Undefined, TmdbRuleField.Genre, "Horror", "ContentType")]
    [InlineData(ContentType.Vod, (TmdbRuleField)99, "Horror", "Field")]
    [InlineData(ContentType.Vod, null, "Horror", "Field")]
    [InlineData(ContentType.Vod, TmdbRuleField.Genre, " ", "Pattern")]
    public async Task PostAndPutRule_WhenInvalid_ReturnValidationProblem(ContentType contentType, TmdbRuleField? field, string pattern, string invalidField)
    {
        var service = new TmdbRuleAdminService(_dbContext);
        var request = Request(contentType, pattern) with { Field = field };

        var postResult = await TmdbRuleEndpoints.PostAsync(request, service, TestContext.Current.CancellationToken);
        var putResult = await TmdbRuleEndpoints.PutAsync(1, request, service, TestContext.Current.CancellationToken);

        Assert.Contains(invalidField, Assert.IsType<ValidationProblem>(postResult).ProblemDetails.Errors.Keys);
        Assert.IsType<ValidationProblem>(putResult);
        Assert.False(await _dbContext.TmdbRules.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostRule_WhenSequenceIsUsed_ReturnsConflict()
    {
        var service = new TmdbRuleAdminService(_dbContext);
        await TmdbRuleEndpoints.PostAsync(Request(ContentType.Vod, "Horror"), service, TestContext.Current.CancellationToken);

        var result = await TmdbRuleEndpoints.PostAsync(Request(ContentType.Vod, "Comedy"), service, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    [Fact]
    public async Task PutAndDeleteRule_WhenRuleDoesNotExist_ReturnNotFound()
    {
        var service = new TmdbRuleAdminService(_dbContext);

        Assert.IsType<NotFound>(await TmdbRuleEndpoints.PutAsync(999, Request(ContentType.Vod, "x"), service, TestContext.Current.CancellationToken));
        Assert.IsType<NotFound>(await TmdbRuleEndpoints.DeleteAsync(999, service, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PutRuleOrder_WhenIdsAreIncomplete_ReturnsValidationProblem()
    {
        var service = new TmdbRuleAdminService(_dbContext);
        await TmdbRuleEndpoints.PostAsync(Request(ContentType.Vod, "Horror"), service, TestContext.Current.CancellationToken);

        var result = await TmdbRuleEndpoints.PutOrderAsync(new AdminRuleOrderRequest(ContentType.Vod, []), service, TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
    }

    // ─── TMDB infos ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInfos_ReturnsAPageWithPosterUrlsAndDecision()
    {
        _dbContext.TmdbInfos.Add(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", PosterPath = "/matrix.jpg", IsExcluded = true, NextLoadAtUtc = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await TmdbInfoEndpoints.GetListAsync(new TmdbInfoListQuery(ContentType.Vod), new TmdbInfoAdminService(_dbContext), TmdbOptions, TestContext.Current.CancellationToken);

        var page = Assert.IsType<Ok<AdminTmdbInfoPageDto>>(result).Value;
        Assert.NotNull(page);
        var info = Assert.Single(page.Items);
        Assert.Equal(
            ("https://image.tmdb.org/t/p/w92/matrix.jpg", "https://image.tmdb.org/t/p/w342/matrix.jpg", InclusionDecision.Exclude, (TmdbExclusionReason?)TmdbExclusionReason.ManuallyExcluded),
            (info.PosterThumbnailUrl, info.PosterUrl, info.Decision, info.ExclusionReason));
        Assert.Equal((1, 1, 1), (page.TotalCount, page.ExcludedCount, page.NotLoadedCount));
    }

    [Fact]
    public async Task GetInfos_WhenContentTypeIsInvalid_ReturnsValidationProblem()
    {
        var result = await TmdbInfoEndpoints.GetListAsync(new TmdbInfoListQuery(ContentType.Undefined), new TmdbInfoAdminService(_dbContext), TmdbOptions, TestContext.Current.CancellationToken);

        Assert.IsType<ValidationProblem>(result);
    }

    [Fact]
    public async Task GetInfoAndPatchInfo_ReturnDetailsOrNotFound()
    {
        var stored = new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", Overview = "Neo", Directors = ["Lana Wachowski"], DurationMinutes = 136, NextLoadAtUtc = DateTimeOffset.UtcNow };
        _dbContext.TmdbInfos.Add(stored);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
        var service = new TmdbInfoAdminService(_dbContext);

        var details = Assert.IsType<Ok<AdminTmdbInfoDetailsDto>>(await TmdbInfoEndpoints.GetAsync(stored.Id, service, TmdbOptions, TestContext.Current.CancellationToken)).Value;
        var patched = await TmdbInfoEndpoints.PatchAsync(stored.Id, new AdminTmdbInfoPatchRequest(true), service, TestContext.Current.CancellationToken);

        Assert.NotNull(details);
        Assert.Equal(("Neo", "Lana Wachowski", 136), (details.Overview, Assert.Single(details.Directors), details.DurationMinutes));
        Assert.IsType<NoContent>(patched);
        Assert.IsType<NotFound>(await TmdbInfoEndpoints.GetAsync(999, service, TmdbOptions, TestContext.Current.CancellationToken));
        Assert.IsType<NotFound>(await TmdbInfoEndpoints.PatchAsync(999, new AdminTmdbInfoPatchRequest(true), service, TestContext.Current.CancellationToken));
        Assert.True((await _dbContext.TmdbInfos.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).IsExcluded);
    }

    // ─── Stream TMDB mappings ───────────────────────────────────────────────

    [Fact]
    public async Task GetMappings_ReturnsAPageWithPosterUrl()
    {
        var mapping = await SeedMappingAsync();
        var service = new StreamTmdbMappingAdminService(_dbContext, new TmdbInfoQueue());

        var result = await StreamTmdbMappingEndpoints.GetListAsync(mapping.XtreamSourceId, new StreamTmdbMappingListQuery(ContentType.Vod), service, TmdbOptions, TestContext.Current.CancellationToken);

        var page = Assert.IsType<Ok<AdminStreamTmdbMappingPageDto>>(result).Value;
        Assert.NotNull(page);
        var item = Assert.Single(page.Items);
        Assert.Equal(("1", (long?)603, "Matrix", "https://image.tmdb.org/t/p/w92/matrix.jpg"), (item.StreamId, item.TmdbId, item.Title, item.PosterThumbnailUrl));
        Assert.Equal((1, 1, 1), (page.MatchingCount, page.TotalCount, page.MappedCount));
    }

    [Fact]
    public async Task GetMappings_WhenContentTypeIsInvalidOrSourceDoesNotExist_ReturnsValidationProblemOrNotFound()
    {
        var service = new StreamTmdbMappingAdminService(_dbContext, new TmdbInfoQueue());

        Assert.IsType<ValidationProblem>(await StreamTmdbMappingEndpoints.GetListAsync(1, new StreamTmdbMappingListQuery(ContentType.Undefined), service, TmdbOptions, TestContext.Current.CancellationToken));
        Assert.IsType<NotFound>(await StreamTmdbMappingEndpoints.GetListAsync(999, new StreamTmdbMappingListQuery(ContentType.Vod), service, TmdbOptions, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PatchMapping_WhenTmdbIdIsNotPositive_ReturnsValidationProblemWithoutChange(long tmdbId)
    {
        var mapping = await SeedMappingAsync();
        var service = new StreamTmdbMappingAdminService(_dbContext, new TmdbInfoQueue());

        var result = await StreamTmdbMappingEndpoints.PatchAsync(mapping.Id, new AdminStreamTmdbMappingPatchRequest(tmdbId), service, TestContext.Current.CancellationToken);

        Assert.Contains("TmdbId", Assert.IsType<ValidationProblem>(result).ProblemDetails.Errors.Keys);
        Assert.Equal(603, (await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).TmdbId);
    }

    [Fact]
    public async Task PatchMapping_ReturnsNoContentOrNotFound()
    {
        var mapping = await SeedMappingAsync();
        var service = new StreamTmdbMappingAdminService(_dbContext, new TmdbInfoQueue());

        var patched = await StreamTmdbMappingEndpoints.PatchAsync(mapping.Id, new AdminStreamTmdbMappingPatchRequest(604), service, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(patched);
        Assert.Equal(604, (await _dbContext.StreamTmdbMappings.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).TmdbId);
        Assert.IsType<NotFound>(await StreamTmdbMappingEndpoints.PatchAsync(999, new AdminStreamTmdbMappingPatchRequest(604), service, TestContext.Current.CancellationToken));
    }

    private async Task<StreamTmdbMapping> SeedMappingAsync()
    {
        var source = new XtreamSource { Protocol = "http", Host = "provider.example.com", Port = 8080 };
        var mapping = new StreamTmdbMapping { ContentType = ContentType.Vod, StreamId = "1", TmdbId = 603 };
        source.StreamTmdbMappings.Add(mapping);
        _dbContext.XtreamSources.Add(source);
        _dbContext.TmdbInfos.Add(new TmdbInfo { TmdbId = 603, ContentType = ContentType.Vod, Title = "Matrix", PosterPath = "/matrix.jpg", NextLoadAtUtc = DateTimeOffset.UtcNow });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();

        return mapping;
    }

    private static AdminRuleRequest Request(ContentType contentType, string pattern) =>
        new(contentType, 10, RuleAction.Exclude, RuleOperator.Contains, pattern, CaseSensitive: false, IsEnabled: true, TmdbRuleField.Genre);

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
