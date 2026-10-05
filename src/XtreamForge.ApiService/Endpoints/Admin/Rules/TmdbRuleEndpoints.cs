using XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.Rules;

public static class TmdbRuleEndpoints
{
    public static async Task<IResult> GetAsync(ContentType contentType, TmdbRuleAdminService tmdbRuleAdminService, CancellationToken cancellationToken = default)
    {
        var rules = await tmdbRuleAdminService.GetAsync(contentType, cancellationToken);

        return TypedResults.Ok(rules.Select(AdminRuleDto.FromRule));
    }

    public static async Task<IResult> PostAsync(AdminRuleRequest request, TmdbRuleAdminService tmdbRuleAdminService, CancellationToken cancellationToken = default)
    {
        if (request.Validate(requiresField: true) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (rule, sequenceConflict) = await tmdbRuleAdminService.CreateAsync(request.ContentType, request.Field.GetValueOrDefault(), request.ToValues(), cancellationToken);
        if (sequenceConflict || rule is null)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Created($"/api/admin/tmdb-rules/{rule.Id}", AdminRuleDto.FromRule(rule));
    }

    public static async Task<IResult> PutAsync(int id, AdminRuleRequest request, TmdbRuleAdminService tmdbRuleAdminService, CancellationToken cancellationToken = default)
    {
        if (request.Validate(requiresField: true) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (found, sequenceConflict) = await tmdbRuleAdminService.UpdateAsync(id, request.Field.GetValueOrDefault(), request.ToValues(), cancellationToken);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return sequenceConflict ? TypedResults.Conflict() : TypedResults.NoContent();
    }

    public static async Task<IResult> PutOrderAsync(AdminRuleOrderRequest request, TmdbRuleAdminService tmdbRuleAdminService, CancellationToken cancellationToken = default)
    {
        if (request.ContentType == ContentType.Undefined || !Enum.IsDefined(request.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminRuleOrderRequest.ContentType)] = ["The content type is not valid."]
            });
        }

        var (result, rules) = await tmdbRuleAdminService.ReorderAsync(request.ContentType, request.RuleIds ?? [], cancellationToken);

        return result == RuleReorderResult.Reordered
            ? TypedResults.Ok(rules.Select(AdminRuleDto.FromRule))
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminRuleOrderRequest.RuleIds)] = ["The rule ids must list every rule of the content type exactly once."]
            });
    }

    public static async Task<IResult> DeleteAsync(int id, TmdbRuleAdminService tmdbRuleAdminService, CancellationToken cancellationToken = default)
    {
        var deleted = await tmdbRuleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
