using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.Admin;

public class CustomCategoryAdminServiceTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly CustomCategoryAdminService _service;

    public CustomCategoryAdminServiceTests()
    {
        _dbContext = SqliteDbContextFactory.Create();

        _service = new CustomCategoryAdminService(_dbContext);
    }

    // ─── GetAllAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyCategoriesMatchingContentType()
    {
        _dbContext.CustomCategories.AddRange(
            new CustomCategory { Name = "Vod A", ContentType = ContentType.Vod },
            new CustomCategory { Name = "Vod B", ContentType = ContentType.Vod },
            new CustomCategory { Name = "Series A", ContentType = ContentType.Series });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetAllAsync(ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.All(result, c => Assert.Equal(ContentType.Vod, c.ContentType));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsCategoriesOrderedByName()
    {
        _dbContext.CustomCategories.AddRange(
            new CustomCategory { Name = "Zebra", ContentType = ContentType.Vod },
            new CustomCategory { Name = "Alpha", ContentType = ContentType.Vod },
            new CustomCategory { Name = "Mango", ContentType = ContentType.Vod });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _service.GetAllAsync(ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "Mango", "Zebra"], result.Select(c => c.Name).ToList());
    }

    // ─── CreateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithUniqueName_ReturnsCategoryAndNoConflict()
    {
        var (category, nameConflict) = await _service.CreateAsync("Action", ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.False(nameConflict);
        Assert.NotNull(category);
        Assert.Equal("Action", category.Name);
        Assert.Equal(ContentType.Vod, category.ContentType);
    }

    [Fact]
    public async Task CreateAsync_WithUniqueName_PersistsToDatabase()
    {
        await _service.CreateAsync("Action", ContentType.Vod, TestContext.Current.CancellationToken);

        var saved = await _dbContext.CustomCategories.SingleAsync(c => c.Name == "Action", TestContext.Current.CancellationToken);
        Assert.Equal(ContentType.Vod, saved.ContentType);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateNameAndSameContentType_ReturnsNameConflict()
    {
        _dbContext.CustomCategories.Add(new CustomCategory { Name = "Action", ContentType = ContentType.Vod });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (category, nameConflict) = await _service.CreateAsync("Action", ContentType.Vod, TestContext.Current.CancellationToken);

        Assert.True(nameConflict);
        Assert.Null(category);
    }

    [Fact]
    public async Task CreateAsync_WithSameNameButDifferentContentType_Succeeds()
    {
        _dbContext.CustomCategories.Add(new CustomCategory { Name = "Action", ContentType = ContentType.Vod });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (category, nameConflict) = await _service.CreateAsync("Action", ContentType.Series, TestContext.Current.CancellationToken);

        Assert.False(nameConflict);
        Assert.NotNull(category);
    }

    // ─── UpdateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ReturnsNotFound()
    {
        var (found, _) = await _service.UpdateAsync(999, "NewName", TestContext.Current.CancellationToken);

        Assert.False(found);
    }

    [Fact]
    public async Task UpdateAsync_WithUniqueName_UpdatesName()
    {
        var category = new CustomCategory { Name = "Old Name", ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (found, nameConflict) = await _service.UpdateAsync(category.Id, "New Name", TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.False(nameConflict);
        var updated = await _dbContext.CustomCategories.FindAsync([category.Id], TestContext.Current.CancellationToken);
        Assert.Equal("New Name", updated!.Name);
    }

    [Fact]
    public async Task UpdateAsync_WithSameNameAsAnother_ReturnsNameConflict()
    {
        _dbContext.CustomCategories.AddRange(
            new CustomCategory { Name = "Taken", ContentType = ContentType.Vod },
            new CustomCategory { Name = "Mine", ContentType = ContentType.Vod });
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var mine = await _dbContext.CustomCategories.SingleAsync(c => c.Name == "Mine", TestContext.Current.CancellationToken);
        var (found, nameConflict) = await _service.UpdateAsync(mine.Id, "Taken", TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.True(nameConflict);
    }

    [Fact]
    public async Task UpdateAsync_WithOwnCurrentName_Succeeds()
    {
        var category = new CustomCategory { Name = "Unchanged", ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var (found, nameConflict) = await _service.UpdateAsync(category.Id, "Unchanged", TestContext.Current.CancellationToken);

        Assert.True(found);
        Assert.False(nameConflict);
    }

    // ─── DeleteAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenExists_ReturnsTrueAndRemovesFromDatabase()
    {
        var category = new CustomCategory { Name = "ToDelete", ContentType = ContentType.Vod };
        _dbContext.CustomCategories.Add(category);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var deleted = await _service.DeleteAsync(category.Id, TestContext.Current.CancellationToken);

        Assert.True(deleted);
        Assert.Null(await _dbContext.CustomCategories.FindAsync([category.Id], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsFalse()
    {
        var deleted = await _service.DeleteAsync(999, TestContext.Current.CancellationToken);

        Assert.False(deleted);
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }
}
