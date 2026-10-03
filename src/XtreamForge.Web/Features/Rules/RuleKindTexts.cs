using XtreamForge.Web.Features.Categories;

namespace XtreamForge.Web.Features.Rules;

/// <summary>Texts of the rules screen that depend on what the rules filter (categories, items, or items by their TMDB metadata).</summary>
public sealed record RuleKindTexts(
    string Title,
    string Subtitle,
    string Singular,
    string Plural,
    string HelpSummary,
    string HelpDetails,
    string EmptyDescription,
    string PatternPlaceholder)
{
    private static readonly RuleKindTexts CategoryTexts = new(
        Title: "Category rules",
        Subtitle: "Include or exclude provider categories by name. Rules are evaluated in order and the first match wins.",
        Singular: "category",
        Plural: "categories",
        HelpSummary: "Categories that no rule matches are included. Disabled rules are skipped.",
        HelpDetails: "Manually excluded categories and categories disabled by the provider are excluded before any rule is evaluated.",
        EmptyDescription: "Without rules, every category of this source and content type is included, except the ones excluded manually or disabled by the provider.",
        PatternPlaceholder: "For example: Kids");

    private static readonly RuleKindTexts ItemTexts = new(
        Title: "Item rules",
        Subtitle: "Include or exclude movies and series by name, within the categories sent to clients. Rules are evaluated in order and the first match wins.",
        Singular: "item",
        Plural: "items",
        HelpSummary: "Items that no rule matches are included. Disabled rules are skipped.",
        HelpDetails: "Item rules only apply to the items of the categories sent to clients; items without a name are always excluded.",
        EmptyDescription: "Without rules, every item of this source and content type is included, except the items without a name.",
        PatternPlaceholder: "For example: [XXX]");

    private static readonly RuleKindTexts TmdbTexts = new(
        Title: "TMDB rules",
        Subtitle: "Include or exclude movies and series by their TMDB title or genre, for every source. Rules are evaluated in order and the first match wins.",
        Singular: "item",
        Plural: "items",
        HelpSummary: "Items that no rule matches are included. Disabled rules are skipped.",
        HelpDetails: "TMDB rules are evaluated once the TMDB metadata of an item is known, after the item rules; manually excluded TMDB entries are excluded before any rule is evaluated. A genre rule matches when one of the genres matches (\"does not …\": when none does); genres are the English TMDB names.",
        EmptyDescription: "Without rules, every item with TMDB metadata is included, except the TMDB entries excluded manually.",
        PatternPlaceholder: "For example: Horror");

    public static RuleKindTexts For(RuleKind kind) => kind switch
    {
        RuleKind.Item => ItemTexts,
        RuleKind.Tmdb => TmdbTexts,
        _ => CategoryTexts
    };

    /// <summary>What the rule matches, as used in the rule sentences ("name", "TMDB title", "TMDB genre").</summary>
    public static string FieldName(RuleKind kind, TmdbRuleField? field) => (kind, field) switch
    {
        (RuleKind.Tmdb, TmdbRuleField.Genre) => "TMDB genre",
        (RuleKind.Tmdb, _) => "TMDB title",
        _ => "name"
    };
}
