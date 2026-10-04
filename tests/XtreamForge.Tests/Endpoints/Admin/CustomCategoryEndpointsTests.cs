using Microsoft.AspNetCore.Http.HttpResults;
using XtreamForge.ApiService.Endpoints.Admin.CustomCategories;
using XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class CustomCategoryEndpointsTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly CustomCategoryAdminService _customCategoryAdminService;

    public CustomCategoryEndpointsTests()
    {
        _dbContext = SqliteDbContextFactory.Create();
        _customCategoryAdminService = new CustomCategoryAdminService(_dbContext);
    }

    // ─── GET /api/admin/custom-categories ───────────────────────────────────

    [Fact]
    public async Task GetAsync_ReturnsOkWithCategoriesOfContentType()
    {
        var vod = await AddCustomCategoryAsync("Movies", ContentType.Vod);
        await AddCustomCategoryAsync("Shows", ContentType.Series);

        var result = await CustomCategoryEndpoints.GetAsync(ContentType.Vod, _customCategoryAdminService, TestContext.Current.CancellationToken);

        var ok = Assert.IsType<Ok<IEnumerable<AdminCustomCategoryDto>>>(result);
        Assert.NotNull(ok.Value);
        Assert.Equal([new AdminCustomCategoryDto(vod.Id, "Movies", ContentType.Vod)], ok.Value);
    }

    // ─── POST /api/admin/custom-categories ──────────────────────────────────

    [Fact]
    public async Task PostAsync_WhenNameIsUnique_ReturnsCreatedWithLocationAndDto()
    {
        var result = await CustomCategoryEndpoints.PostAsync(new AdminCustomCategoryRequest("Movies", ContentType.Vod), _customCategoryAdminService, TestContext.Current.CancellationToken);

        var created = Assert.IsType<Created<AdminCustomCategoryDto>>(result);
        Assert.NotNull(created.Value);
        Assert.Equal($"/api/admin/custom-categories/{created.Value.Id}", created.Location);
        Assert.Equal("Movies", created.Value.Name);
        Assert.Equal(ContentType.Vod, created.Value.ContentType);
    }

    [Fact]
    public async Task PostAsync_WhenNameAlreadyExists_ReturnsConflict()
    {
        await AddCustomCategoryAsync("Movies", ContentType.Vod);

        var result = await CustomCategoryEndpoints.PostAsync(new AdminCustomCategoryRequest("Movies", ContentType.Vod), _customCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── PUT /api/admin/custom-categories/{id} ──────────────────────────────

    [Fact]
    public async Task PutAsync_WhenValid_ReturnsNoContent()
    {
        var category = await AddCustomCategoryAsync("Movies", ContentType.Vod);

        var result = await CustomCategoryEndpoints.PutAsync(category.Id, new AdminCustomCategoryRequest("Films", ContentType.Vod), _customCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task PutAsync_WhenCategoryDoesNotExist_ReturnsNotFound()
    {
        var result = await CustomCategoryEndpoints.PutAsync(999, new AdminCustomCategoryRequest("Films", ContentType.Vod), _customCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task PutAsync_WhenNameAlreadyExists_ReturnsConflict()
    {
        await AddCustomCategoryAsync("Movies", ContentType.Vod);
        var category = await AddCustomCategoryAsync("Films", ContentType.Vod);

        var result = await CustomCategoryEndpoints.PutAsync(category.Id, new AdminCustomCategoryRequest("Movies", ContentType.Vod), _customCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<Conflict>(result);
    }

    // ─── DELETE /api/admin/custom-categories/{id} ───────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenCategoryExists_ReturnsNoContent()
    {
        var category = await AddCustomCategoryAsync("Movies", ContentType.Vod);

        var result = await CustomCategoryEndpoints.DeleteAsync(category.Id, _customCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenCategoryDoesNotExist_ReturnsNotFound()
    {
        var result = await CustomCategoryEndpoints.DeleteAsync(999, _customCategoryAdminService, TestContext.Current.CancellationToken);

        Assert.IsType<NotFound>(result);
    }

    private async Task<CustomCategory> AddCustomCategoryAsync(string name, ContentType contentType)
    {
        var category = new CustomCategory { Name = name, ContentType = contentType };
        _dbContext.CustomCategories.Add(category);
        await _dbContext.SaveChangesAsync();
        return category;
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
