using XtreamForge.Domain.Sources;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>A source with the counters displayed by the administration UI, per content type.</summary>
public sealed record SourceSummary(XtreamSource Source, SourceContentSummary Vod, SourceContentSummary Series);

/// <summary>
/// Counters of one source and content type. Every Xtream category falls in exactly one of
/// <paramref name="EffectiveCategories"/>, <paramref name="ManuallyExcludedCategories"/>,
/// <paramref name="ProviderDisabledCategories"/> and <paramref name="RuleExcludedCategories"/>
/// (the decision of the category filtering, see <c>RuleEvaluator.EvaluateCategories</c>).
/// </summary>
public sealed record SourceContentSummary(
    int Categories,
    int EffectiveCategories,
    int ManuallyExcludedCategories,
    int ProviderDisabledCategories,
    int RuleExcludedCategories,
    int MappedCategories,
    int CategoryRules,
    int ItemRules,
    int TmdbMappings);
