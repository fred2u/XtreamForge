namespace XtreamForge.Configuration;

public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    public string ApiKey { get; init; } = string.Empty;
}
