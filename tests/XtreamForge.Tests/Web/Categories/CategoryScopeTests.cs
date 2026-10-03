using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Tests.Web.Categories;

public class CategoryScopeTests
{
    [Fact]
    public void Resolve_WhenNothingIsRequestedOrRemembered_ReturnsFirstSourceAndFirstContentType()
    {
        var scope = new CategoryScope();

        var (sourceId, contentType) = scope.Resolve([3, 7], null, null);

        Assert.Equal(3, sourceId);
        Assert.Equal(CategoryLabels.ContentTypes[0], contentType);
    }

    [Fact]
    public void Resolve_WhenNothingIsRequested_ReturnsRememberedScope()
    {
        var scope = new CategoryScope { SourceId = 7, ContentType = ContentType.Series };

        var (sourceId, contentType) = scope.Resolve([3, 7], null, null);

        Assert.Equal(7, sourceId);
        Assert.Equal(ContentType.Series, contentType);
    }

    [Fact]
    public void Resolve_WhenScopeIsRequested_ReturnsRequestedScope()
    {
        var scope = new CategoryScope { SourceId = 7, ContentType = ContentType.Series };

        var (sourceId, contentType) = scope.Resolve([3, 7], 3, ContentType.Vod);

        Assert.Equal(3, sourceId);
        Assert.Equal(ContentType.Vod, contentType);
    }

    [Fact]
    public void Resolve_WhenRequestedSourceIsUnknown_FallsBackToRememberedSource()
    {
        var scope = new CategoryScope { SourceId = 7 };

        var (sourceId, _) = scope.Resolve([3, 7], 99, null);

        Assert.Equal(7, sourceId);
    }

    [Fact]
    public void Resolve_WhenRememberedSourceNoLongerExists_ReturnsFirstSource()
    {
        var scope = new CategoryScope { SourceId = 42 };

        var (sourceId, _) = scope.Resolve([3, 7], null, null);

        Assert.Equal(3, sourceId);
    }

    [Fact]
    public void Resolve_WhenThereIsNoSource_ReturnsNoSource()
    {
        var scope = new CategoryScope();

        var (sourceId, _) = scope.Resolve([], null, null);

        Assert.Null(sourceId);
    }
}
