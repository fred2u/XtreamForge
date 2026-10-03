using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin.ItemRules;

public class ItemRulesGetEndpoint(ItemRuleAdminService itemRuleAdminService, SourceAdminService sourceAdminService)
{
    public async Task<IResult> GetAsync(int sourceId, ContentType contentType, CancellationToken cancellationToken = default)
    {
        var source = await sourceAdminService.FindAsync(sourceId, cancellationToken);
        if (source is null)
        {
            return TypedResults.NotFound();
        }

        var rules = await itemRuleAdminService.GetBySourceAsync(sourceId, contentType, cancellationToken);

        var dtos = rules.Select(r => new AdminItemRuleDto(
            r.Id,
            r.XtreamSourceId,
            r.ContentType,
            r.Sequence,
            r.Action,
            r.Operator,
            r.Pattern,
            r.CaseSensitive,
            r.IsEnabled));

        return TypedResults.Ok(dtos);
    }
}
