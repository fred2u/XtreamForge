using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.CategoryRules;

public static class CategoryRuleEndpoints
{
    public static async Task<IResult> GetAsync(
        int sourceId,
        ContentType contentType,
        CategoryRuleAdminService categoryRuleAdminService,
        SourceAdminService sourceAdminService,
        CancellationToken cancellationToken = default)
    {
        var source = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var rules = await categoryRuleAdminService.GetBySourceAsync(sourceId, contentType, cancellationToken);

        return TypedResults.Ok(rules.Select(AdminCategoryRuleDto.FromRule));
    }

    public static async Task<IResult> PostAsync(int sourceId, AdminCategoryRuleRequest request, CategoryRuleAdminService categoryRuleAdminService, CancellationToken cancellationToken = default)
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

        if (sequenceConflict)
        {
            return TypedResults.Conflict();
        }

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Created($"/api/admin/sources/{sourceId}/category-rules/{rule.Id}", AdminCategoryRuleDto.FromRule(rule));
    }

    public static async Task<IResult> PutAsync(int id, AdminCategoryRuleRequest request, CategoryRuleAdminService categoryRuleAdminService, CancellationToken cancellationToken = default)
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

    public static async Task<IResult> PutOrderAsync(int sourceId, AdminCategoryRuleOrderRequest request, CategoryRuleAdminService categoryRuleAdminService, CancellationToken cancellationToken = default)
    {
        if (request.ContentType == ContentType.Undefined || !Enum.IsDefined(request.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminCategoryRuleOrderRequest.ContentType)] = ["The content type is not valid."]
            });
        }

        var (result, rules) = await categoryRuleAdminService.ReorderAsync(sourceId, request.ContentType, request.RuleIds ?? [], cancellationToken);

        return result switch
        {
            RuleReorderResult.Reordered => TypedResults.Ok(rules.Select(AdminCategoryRuleDto.FromRule)),
            RuleReorderResult.InvalidOrder => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminCategoryRuleOrderRequest.RuleIds)] = ["The rule ids must list every rule of the source and content type exactly once."]
            }),
            _ => TypedResults.NotFound()
        };
    }

    public static async Task<IResult> DeleteAsync(int id, CategoryRuleAdminService categoryRuleAdminService, CancellationToken cancellationToken = default)
    {
        var deleted = await categoryRuleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
