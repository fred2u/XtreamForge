namespace XtreamForge.Domain.Enums;

/// <summary>Outcome of the atomic reorder of the rules of a scope (a content type, and a source for the category and item rules).</summary>
public enum RuleReorderResult
{
    Reordered,
    SourceNotFound,
    InvalidOrder
}
