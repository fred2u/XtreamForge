namespace XtreamForge.Domain.Enums;

/// <summary>Outcome of the atomic reorder of the category rules or item rules of a source and content type.</summary>
public enum RuleReorderResult
{
    Reordered,
    SourceNotFound,
    InvalidOrder
}
