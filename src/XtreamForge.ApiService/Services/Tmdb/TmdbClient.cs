using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Tmdb;

public class TmdbClient(IHttpClientFactory httpClientFactory, IOptions<TmdbOptions> options)
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private string Language => Uri.EscapeDataString(options.Value.PreferredLanguage);

    /// <summary>
    /// Searches with the year first and falls back to the title only when nothing is found.
    /// </summary>
    public async Task<IReadOnlyList<long>> SearchAsync(ContentType type, string title, int? year, CancellationToken cancellationToken)
    {
        var (path, yearParameter) = type == ContentType.Vod
            ? ("search/movie", "year")
            : ("search/tv", "first_air_date_year");

        var url = $"{path}?query={Uri.EscapeDataString(title)}&language={Language}";

        if (year is not null)
        {
            var results = await SearchAsync($"{url}&{yearParameter}={year.Value.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
            if (results.Count > 0)
                return results;
        }

        return await SearchAsync(url, cancellationToken);
    }

    /// <summary>
    /// Returns the metadata used to enrich the items, including the credits and the duration, or null when TMDB does not know the ID.
    /// </summary>
    public async Task<TmdbInfoData?> GetInfoAsync(ContentType type, long tmdbId, CancellationToken cancellationToken)
    {
        var path = type == ContentType.Vod ? "movie" : "tv";

        var details = await GetAsync<TmdbInfoResponse>($"{path}/{tmdbId.ToString(CultureInfo.InvariantCulture)}?language={Language}&append_to_response=credits", cancellationToken);

        return details?.ToInfoData(type);
    }

    public async Task<TmdbCandidate?> GetCandidateAsync(ContentType type, long tmdbId, CancellationToken cancellationToken)
    {
        var path = type == ContentType.Vod ? "movie" : "tv";

        var details = await GetAsync<TmdbDetailsResponse>($"{path}/{tmdbId.ToString(CultureInfo.InvariantCulture)}?language={Language}&append_to_response=credits", cancellationToken);
        if (details is null)
            return null;

        return new TmdbCandidate
        {
            Id = details.Id,
            Title = details.Title ?? details.Name ?? string.Empty,
            OriginalTitle = details.OriginalTitle ?? details.OriginalName ?? string.Empty,
            ReleaseDate = TmdbText.ParseDate(details.ReleaseDate ?? details.FirstAirDate),
            PosterPath = details.PosterPath,
            Cast = (details.Credits?.Cast ?? [])
                .Select(member => TmdbText.Normalize(member.Name))
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.Ordinal),
            Genres = (details.Genres ?? [])
                .SelectMany(genre => new[] { genre.Name, TmdbGenres.GetEnglishName(genre.Id) })
                .Select(TmdbText.Normalize)
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.Ordinal)
        };
    }

    /// <summary>
    /// Returns the episodes of a TV season, or null when TMDB does not know the season.
    /// </summary>
    public async Task<IReadOnlyList<TmdbEpisode>?> GetSeasonEpisodesAsync(long tvId, int seasonNumber, CancellationToken cancellationToken)
    {
        var season = await GetAsync<TmdbSeasonResponse>(
            $"tv/{tvId.ToString(CultureInfo.InvariantCulture)}/season/{seasonNumber.ToString(CultureInfo.InvariantCulture)}?language={Language}",
            cancellationToken);

        if (season is null)
            return null;

        return (season.Episodes ?? [])
            .Select(episode => new TmdbEpisode(episode.EpisodeNumber, TmdbText.ParseDate(episode.AirDate)))
            .ToList();
    }

    /// <summary>
    /// Returns the first page of the movies recommended by TMDB for a movie, in TMDB order, or an empty list when TMDB does not know the movie.
    /// </summary>
    public async Task<IReadOnlyList<TmdbRecommendation>> GetMovieRecommendationsAsync(long movieId, CancellationToken cancellationToken)
    {
        var response = await GetAsync<TmdbRecommendationsResponse>(
            $"movie/{movieId.ToString(CultureInfo.InvariantCulture)}/recommendations?language={Language}",
            cancellationToken);

        return [.. (response?.Results ?? [])
            .Where(result => result.Id > 0)
            .Select(result => result.ToRecommendation())];
    }

    /// <summary>Returns the IDs of a page (from 1) of the movies or TV shows currently popular on TMDB, in TMDB order.</summary>
    public async Task<IReadOnlyList<long>> GetPopularAsync(ContentType type, int page, CancellationToken cancellationToken)
    {
        var path = type == ContentType.Vod ? "movie/popular" : "tv/popular";

        return await GetResultIdsAsync($"{path}?language={Language}&page={page.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
    }

    private async Task<List<long>> SearchAsync(string url, CancellationToken cancellationToken)
        => await GetResultIdsAsync(url, cancellationToken);

    // search and list results share the same shape: { "results": [ { "id": ... } ] }
    private async Task<List<long>> GetResultIdsAsync(string url, CancellationToken cancellationToken)
    {
        var response = await GetAsync<TmdbSearchResponse>(url, cancellationToken);

        return [.. (response?.Results ?? []).Select(result => result.Id).Where(id => id > 0)];
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient(TmdbOptions.HttpClientName);

        using var response = await httpClient.GetAsync(new Uri(url, UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
    }

    private sealed record TmdbSearchResponse(List<TmdbSearchResult>? Results);

    private sealed record TmdbSearchResult(long Id);

    private sealed record TmdbRecommendationsResponse(List<TmdbRecommendationResult>? Results);

    private sealed record TmdbRecommendationResult(
        long Id,
        string? Title,
        string? OriginalTitle,
        string? ReleaseDate,
        string? PosterPath,
        double? VoteAverage,
        int? VoteCount,
        List<int>? GenreIds)
    {
        public TmdbRecommendation ToRecommendation()
        {
            var releaseDate = TmdbText.ParseDate(ReleaseDate);

            return new TmdbRecommendation(
                Id,
                Title,
                OriginalTitle,
                releaseDate is null ? null : DateOnly.FromDateTime(releaseDate.Value),
                PosterPath,
                VoteAverage,
                VoteCount,
                [.. (GenreIds ?? []).Select(TmdbGenres.GetEnglishName).OfType<string>()]);
        }
    }

    // movies use Title/OriginalTitle/ReleaseDate/Runtime, TV shows Name/OriginalName/FirstAirDate/EpisodeRunTime/CreatedBy;
    // the credits come from append_to_response=credits
    private sealed record TmdbInfoResponse(
        long Id,
        string? Title,
        string? OriginalTitle,
        string? ReleaseDate,
        string? Name,
        string? OriginalName,
        string? FirstAirDate,
        string? PosterPath,
        string? Overview,
        double? VoteAverage,
        int? VoteCount,
        List<TmdbGenreResponse>? Genres,
        int? Runtime,
        List<int>? EpisodeRunTime,
        TmdbEpisodeRuntimeResponse? LastEpisodeToAir,
        List<TmdbPersonResponse>? CreatedBy,
        TmdbCreditsResponse? Credits)
    {
        // a TV show has no director: its creators are used instead; its duration is the episode run time
        public TmdbInfoData ToInfoData(ContentType type)
        {
            var releaseDate = TmdbText.ParseDate(ReleaseDate ?? FirstAirDate);
            var directors = type == ContentType.Vod
                ? (Credits?.Crew ?? []).Where(member => member.Job == "Director").Select(member => member.Name)
                : (CreatedBy ?? []).Select(creator => creator.Name);

            int? durationMinutes = Runtime;
            if (type == ContentType.Series)
            {
                // episode_run_time is often empty for recent shows
                var episodeRunTime = (EpisodeRunTime ?? []).FirstOrDefault(runtime => runtime > 0);
                durationMinutes = episodeRunTime > 0 ? episodeRunTime : LastEpisodeToAir?.Runtime;
            }

            return new TmdbInfoData(
                Id,
                Title ?? Name,
                OriginalTitle ?? OriginalName,
                releaseDate is null ? null : DateOnly.FromDateTime(releaseDate.Value),
                PosterPath,
                Overview,
                VoteAverage,
                VoteCount,
                [.. (Genres ?? []).Select(genre => genre.Id)],
                [.. directors.OfType<string>()],
                [.. (Credits?.Cast ?? []).Select(member => member.Name).OfType<string>()],
                durationMinutes);
        }
    }

    // movie and TV details share this model: movies use Title/OriginalTitle/ReleaseDate, TV shows Name/OriginalName/FirstAirDate
    private sealed record TmdbDetailsResponse(
        long Id,
        string? Title,
        string? OriginalTitle,
        string? ReleaseDate,
        string? Name,
        string? OriginalName,
        string? FirstAirDate,
        string? PosterPath,
        List<TmdbGenreResponse>? Genres,
        TmdbCreditsResponse? Credits);

    private sealed record TmdbGenreResponse(int Id, string? Name);

    private sealed record TmdbCreditsResponse(List<TmdbCastMemberResponse>? Cast, List<TmdbCrewMemberResponse>? Crew = null);

    private sealed record TmdbCastMemberResponse(string? Name);

    private sealed record TmdbCrewMemberResponse(string? Name, string? Job);

    private sealed record TmdbPersonResponse(string? Name);

    private sealed record TmdbEpisodeRuntimeResponse(int? Runtime);

    private sealed record TmdbSeasonResponse(List<TmdbEpisodeResponse>? Episodes);

    private sealed record TmdbEpisodeResponse(int EpisodeNumber, string? AirDate);
}
