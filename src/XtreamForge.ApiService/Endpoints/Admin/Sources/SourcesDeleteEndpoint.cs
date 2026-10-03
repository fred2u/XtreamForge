using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.Sources;

public class SourcesDeleteEndpoint(SourceAdminService sourceAdminService)
{
    public async Task<IResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await sourceAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
