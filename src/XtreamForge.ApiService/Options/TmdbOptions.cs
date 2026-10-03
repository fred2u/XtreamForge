namespace XtreamForge.ApiService.Options;

public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";
    public const string HttpClientName = "Tmdb";

    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Base URL of the TMDB images, followed by the size (for example "w342") and the image path.</summary>
    public string ImageBaseUrl { get; init; } = "https://image.tmdb.org/t/p/";

    public string ApiKey { get; init; } = string.Empty;

    public string PreferredLanguage { get; init; } = "fr-FR";

    public int MinimumConfidenceScore { get; init; } = 85;
}
