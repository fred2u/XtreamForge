using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Services.Admin;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules;

public class CategoryRulePostEndpoint(CategoryRuleAdminService categoryRuleAdminService)
{
    public async Task<IResult> PostAsync(int sourceId, AdminCategoryRuleRequest request, CancellationToken cancellationToken = default)
    {
        var (rule, sequenceConflict) = await categoryRuleAdminService.CreateAsync(
            sourceId,
            request.ContentType,
            request.Sequence,
            request.Action,
            request.Operator,
            request.Pattern,
            request.CaseSensitive,
            request.IsEnabled,
            cancellationToken);

        if (rule is null && !sequenceConflict)
        {
            return TypedResults.NotFound();
        }

        if (sequenceConflict)
        {
            return TypedResults.Conflict();
        }

        var dto = new AdminCategoryRuleDto(
            rule!.Id,
            rule.XtreamSourceId,
            rule.ContentType,
            rule.Sequence,
            rule.Action,
            rule.Operator,
            rule.Pattern,
            rule.CaseSensitive,
            rule.IsEnabled);

        return TypedResults.Created($"/api/admin/sources/{sourceId}/category-rules/{rule.Id}", dto);
    }
}
