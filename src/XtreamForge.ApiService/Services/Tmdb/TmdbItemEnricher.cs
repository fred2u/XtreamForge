using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.Tmdb;

/// <summary>
/// Replaces the values of an Xtream object (list item, <c>info</c> or <c>movie_data</c> section) with TMDB metadata.
/// Only keys already present are replaced, and only with usable TMDB values: the rest of the provider payload is left unchanged.
/// The TMDB value takes precedence when both sources have one, except for <c>genre</c>: the TMDB genres are English names,
/// so they only fill an empty provider value.
/// </summary>
public static class TmdbItemEnricher
{
    public const string MediumPosterSize = "w342";
    public const string LargePosterSize = "w780";

    private const string NameSeparator = ", ";

    public static void Apply(JsonObject item, TmdbInfo info, string imageBaseUrl)
    {
        if (info.Title is { } title)
        {
            var formattedTitle = info.ReleaseDate is { } date
                ? $"{title} | {date.Year.ToString(CultureInfo.InvariantCulture)}"
                : title;

            TryReplaceValue(item, "name", formattedTitle);
            TryReplaceValue(item, "o_name", formattedTitle);
            TryReplaceValue(item, "title", formattedTitle);
        }

        if (info.ReleaseDate is { } releaseDate)
        {
            var year = releaseDate.Year.ToString(CultureInfo.InvariantCulture);
            TryReplaceValue(item, "year", year, releaseDate.Year);
            TryReplaceValue(item, "release_date", year, releaseDate.Year);

            var formattedReleaseDate = releaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            TryReplaceValue(item, "releasedate", formattedReleaseDate);
            TryReplaceValue(item, "releaseDate", formattedReleaseDate);
        }

        if (BuildImageUrl(imageBaseUrl, MediumPosterSize, info.PosterPath) is { } mediumPosterUrl
            && BuildImageUrl(imageBaseUrl, LargePosterSize, info.PosterPath) is { } largePosterUrl)
        {
            TryReplaceValue(item, "stream_icon", mediumPosterUrl);
            TryReplaceValue(item, "movie_image", largePosterUrl);
            TryReplaceValue(item, "cover_big", largePosterUrl);
            TryReplaceValue(item, "cover", largePosterUrl);
        }

        // a rating without votes is meaningless
        if (info.VoteAverage is { } voteAverage && info.VoteCount > 0)
        {
            var rating = Math.Round(voteAverage, 1);
            var fiveBasedRating = Math.Round(voteAverage / 2, 1);

            TryReplaceValue(item, "rating", rating.ToString("0.0", CultureInfo.InvariantCulture), rating);
            TryReplaceValue(item, "rating_5based", fiveBasedRating.ToString("0.0", CultureInfo.InvariantCulture), fiveBasedRating);
        }

        if (info.Overview is { } overview)
        {
            TryReplaceValue(item, "plot", overview);
            TryReplaceValue(item, "description", overview);
        }

        if (info.Directors.Count > 0)
        {
            TryReplaceValue(item, "director", string.Join(NameSeparator, info.Directors));
        }

        if (info.Cast.Count > 0)
        {
            var cast = string.Join(NameSeparator, info.Cast);
            TryReplaceValue(item, "cast", cast);
            TryReplaceValue(item, "actors", cast);
        }

        if (info.DurationMinutes is { } durationMinutes)
        {
            var duration = TimeSpan.FromMinutes(durationMinutes);
            var durationSeconds = (int)duration.TotalSeconds;

            TryReplaceValue(item, "duration_secs", durationSeconds.ToString(CultureInfo.InvariantCulture), durationSeconds);
            TryReplaceValue(item, "duration", $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}");
            TryReplaceValue(item, "episode_run_time", durationMinutes.ToString(CultureInfo.InvariantCulture), durationMinutes);
        }

        if (info.Genres.Count > 0 && item.TryGetPropertyValue("genre", out var providerGenre) && IsEmpty(providerGenre))
        {
            TryReplaceValue(item, "genre", string.Join(NameSeparator, info.Genres));
        }
    }

    /// <summary>
    /// Builds a TMDB image URL, or returns null when the path is missing or does not produce a valid absolute HTTP(S) URL.
    /// </summary>
    public static string? BuildImageUrl(string imageBaseUrl, string size, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;

        var url = $"{imageBaseUrl.TrimEnd('/')}/{size}/{imagePath.Trim().TrimStart('/')}";

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : null;
    }

    private static bool IsEmpty(JsonNode? value)
        => value is null or JsonArray { Count: 0 } || string.IsNullOrWhiteSpace(value.ToString());

    // the JSON kind of the provider value is kept when a numeric value is available: numbers stay numbers, everything else becomes a string
    private static void TryReplaceValue(JsonObject item, string key, string text, double? number = null)
    {
        if (!item.TryGetPropertyValue(key, out var currentValue))
            return;

        item[key] = number is not null && currentValue is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            ? JsonValue.Create(number.Value)
            : JsonValue.Create(text);
    }
}
