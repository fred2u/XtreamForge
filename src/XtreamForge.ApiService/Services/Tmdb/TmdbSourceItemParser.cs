using System.Globalization;
using System.Text.Json;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb;

/// <summary>
/// Reads the get_vod_info / get_series_info payload of an Xtream provider.
/// </summary>
public static class TmdbSourceItemParser
{
    public static TmdbSourceItem Parse(ContentType type, JsonElement root, string? streamIcon)
        => type == ContentType.Vod ? ParseMovie(root, streamIcon) : ParseSeries(root);

    private static TmdbSourceItem ParseMovie(JsonElement root, string? streamIcon)
    {
        var info = GetObject(root, "info");
        var movieData = GetObject(root, "movie_data");
        if (info is null || movieData is null)
            return new TmdbSourceItem { Type = ContentType.Vod };

        var rawTitle = GetString(movieData.Value, "name");

        var cover = new[] { streamIcon, GetString(info.Value, "movie_image"), GetString(info.Value, "cover_big"), GetString(info.Value, "cover") }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new TmdbSourceItem
        {
            Type = ContentType.Vod,
            RawTitle = rawTitle,
            Title = Cleaners.CleanTitle(rawTitle),
            PosterId = GetPosterId(cover),
            ReleaseDate = TmdbText.ParseDate(GetString(info.Value, "releaseDate")) ?? TmdbText.ParseDate(GetString(info.Value, "releasedate")),
            Cast = SplitAndNormalize(GetString(info.Value, "cast")),
            Genres = SplitAndNormalize(GetString(info.Value, "genre"))
        };
    }

    private static TmdbSourceItem ParseSeries(JsonElement root)
    {
        var info = GetObject(root, "info");
        if (info is null)
            return new TmdbSourceItem { Type = ContentType.Series };

        var rawTitle = GetString(info.Value, "name");

        return new TmdbSourceItem
        {
            Type = ContentType.Series,
            RawTitle = rawTitle,
            Title = Cleaners.CleanTitle(rawTitle),
            PosterId = GetPosterId(GetString(info.Value, "cover")),
            ReleaseDate = TmdbText.ParseDate(GetString(info.Value, "releaseDate")),
            Cast = SplitAndNormalize(GetString(info.Value, "cast")),
            Genres = SplitAndNormalize(GetString(info.Value, "genre")),
            Seasons = ParseSeasons(root),
            Episodes = ParseEpisodes(root)
        };
    }

    private static List<TmdbSourceSeason> ParseSeasons(JsonElement root)
    {
        if (!root.TryGetProperty("seasons", out var seasons) || seasons.ValueKind != JsonValueKind.Array)
            return [];

        return [.. seasons.EnumerateArray()
            .Where(season => season.ValueKind == JsonValueKind.Object)
            .Select(season => new TmdbSourceSeason(GetInt(season, "season_number"), GetInt(season, "episode_count")))];
    }

    private static List<TmdbSourceEpisode> ParseEpisodes(JsonElement root)
    {
        // "episodes" is an object whose properties are the season numbers, each containing an array of episodes
        if (!root.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.Object)
            return [];

        return [.. episodes.EnumerateObject()
            .Where(season => season.Value.ValueKind == JsonValueKind.Array)
            .SelectMany(season => season.Value.EnumerateArray())
            .Where(episode => episode.ValueKind == JsonValueKind.Object)
            .Select(episode => new TmdbSourceEpisode(
                GetInt(episode, "season"),
                GetInt(episode, "episode_num"),
                GetObject(episode, "info") is { } episodeInfo ? TmdbText.ParseDate(GetString(episodeInfo, "air_date")) : null))];
    }

    private static string GetPosterId(string? url)
        => string.IsNullOrWhiteSpace(url) ? string.Empty : Path.GetFileNameWithoutExtension(url);

    private static HashSet<string> SplitAndNormalize(string value)
        => value
            .Split([',', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TmdbText.Normalize)
            .Where(item => item.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static JsonElement? GetObject(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };
    }

    private static int GetInt(JsonElement element, string property)
        => int.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
}
