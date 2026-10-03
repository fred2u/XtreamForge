using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules;

public class CategoryRulePutEndpoint(CategoryRuleAdminService categoryRuleAdminService)
{
    public async Task<IResult> PutAsync(int id, AdminCategoryRuleRequest request, CancellationToken cancellationToken = default)
    {
        var (found, sequenceConflict) = await categoryRuleAdminService.UpdateAsync(
            id,
            request.Sequence,
            request.Action,
            request.Operator,
            request.Pattern,
            request.CaseSensitive,
            request.IsEnabled,
            cancellationToken);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        if (sequenceConflict)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.NoContent();
    }
}
