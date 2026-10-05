using XtreamForge.ApiService.Endpoints.Admin.Rules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Rules;

namespace XtreamForge.ApiService.Endpoints.Admin.Rules;

/// <summary>Handlers of the rules defined per source, mapped once for the category rules and once for the item rules.</summary>
public static class SourceRuleEndpoints
{
    public static async Task<IResult> GetAsync<TRule>(
        int sourceId,
        ContentType contentType,
        SourceRuleAdminService<TRule> ruleAdminService,
        SourceAdminService sourceAdminService,
        CancellationToken cancellationToken = default)
        where TRule : class, ISourceRule, new()
    {
        var source = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var rules = await ruleAdminService.GetBySourceAsync(sourceId, contentType, cancellationToken);

        return TypedResults.Ok(rules.Select(AdminRuleDto.FromRule));
    }

    /// <summary>The location of the created rule is the request path followed by its id.</summary>
    public static async Task<IResult> PostAsync<TRule>(
        int sourceId,
        AdminRuleRequest request,
        SourceRuleAdminService<TRule> ruleAdminService,
        HttpRequest httpRequest,
        CancellationToken cancellationToken = default)
        where TRule : class, ISourceRule, new()
    {
        if (request.Validate(requiresField: false) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (rule, sequenceConflict) = await ruleAdminService.CreateAsync(sourceId, request.ContentType, request.ToValues(), cancellationToken);

        if (sequenceConflict)
        {
            return TypedResults.Conflict();
        }

        if (rule is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Created($"{httpRequest.Path}/{rule.Id}", AdminRuleDto.FromRule(rule));
    }

    public static async Task<IResult> PutAsync<TRule>(
        int id,
        AdminRuleRequest request,
        SourceRuleAdminService<TRule> ruleAdminService,
        CancellationToken cancellationToken = default)
        where TRule : class, ISourceRule, new()
    {
        if (request.Validate(requiresField: false) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (found, sequenceConflict) = await ruleAdminService.UpdateAsync(id, request.ToValues(), cancellationToken);

        if (!found)
        {
            return TypedResults.NotFound();
        }

        return sequenceConflict ? TypedResults.Conflict() : TypedResults.NoContent();
    }

    public static async Task<IResult> PutOrderAsync<TRule>(
        int sourceId,
        AdminRuleOrderRequest request,
        SourceRuleAdminService<TRule> ruleAdminService,
        CancellationToken cancellationToken = default)
        where TRule : class, ISourceRule, new()
    {
        if (request.ContentType == ContentType.Undefined || !Enum.IsDefined(request.ContentType))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminRuleOrderRequest.ContentType)] = ["The content type is not valid."]
            });
        }

        var (result, rules) = await ruleAdminService.ReorderAsync(sourceId, request.ContentType, request.RuleIds ?? [], cancellationToken);

        return result switch
        {
            RuleReorderResult.Reordered => TypedResults.Ok(rules.Select(AdminRuleDto.FromRule)),
            RuleReorderResult.InvalidOrder => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AdminRuleOrderRequest.RuleIds)] = ["The rule ids must list every rule of the source and content type exactly once."]
            }),
            _ => TypedResults.NotFound()
        };
    }

    public static async Task<IResult> DeleteAsync<TRule>(
        int id,
        SourceRuleAdminService<TRule> ruleAdminService,
        CancellationToken cancellationToken = default)
        where TRule : class, ISourceRule, new()
    {
        var deleted = await ruleAdminService.DeleteAsync(id, cancellationToken);

        return deleted
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }
}
