using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Database;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Sources;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Endpoints.Admin;

public class SourcesDeleteEndpointTests : IAsyncDisposable
{
    private readonly XtreamForgeDbContext _dbContext;
    private readonly SourcesDeleteEndpoint _endpoint;
    private readonly SourceAdminService _sourceAdminService;

    public SourcesDeleteEndpointTests()
    {
        _dbContext = SqliteDbContextFactory.Create();

        _sourceAdminService = new SourceAdminService(_dbContext);
        _endpoint = new SourcesDeleteEndpoint(_sourceAdminService);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceExists_ReturnsNoContent()
    {
        var source = new XtreamSource { Protocol = "http", Host = "source.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await _endpoint.DeleteAsync(source.Id, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceExists_SourceIsRemovedFromDatabase()
    {
        var source = new XtreamSource { Protocol = "http", Host = "source.example.com", Port = 8080 };
        _dbContext.XtreamSources.Add(source);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _endpoint.DeleteAsync(source.Id, TestContext.Current.CancellationToken);

        var remaining = await _sourceAdminService.GetAllAsync(TestContext.Current.CancellationToken);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeleteAsync_WhenSourceDoesNotExist_ReturnsNotFound()
    {
        var result = await _endpoint.DeleteAsync(999, TestContext.Current.CancellationToken);

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

        var result = await _endpoint.DeleteAsync(source.Id, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        Assert.False(await _dbContext.CategoryRules.AnyAsync(r => r.XtreamSourceId == source.Id, TestContext.Current.CancellationToken));
        Assert.False(await _dbContext.ItemRules.AnyAsync(r => r.XtreamSourceId == source.Id, TestContext.Current.CancellationToken));
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

        var result = await _endpoint.DeleteAsync(source.Id, TestContext.Current.CancellationToken);

        Assert.IsType<NoContent>(result);
        Assert.False(await _dbContext.XtreamCategories.AnyAsync(TestContext.Current.CancellationToken));
        Assert.True(await _dbContext.CustomCategories.AnyAsync(c => c.Id == customCategory.Id, TestContext.Current.CancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
