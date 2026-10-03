namespace XtreamForge.ApiService.Services.Tmdb;

/// <summary>
/// English names of the TMDB movie and TV genres, indexed by their stable TMDB identifier.
/// Providers often expose English genres while TMDB is queried in the preferred language,
/// so candidates are compared against both the localized and the English names.
/// </summary>
public static class TmdbGenres
{
    private static readonly Dictionary<int, string> _englishNames = new()
    {
        [28] = "Action",
        [12] = "Adventure",
        [16] = "Animation",
        [35] = "Comedy",
        [80] = "Crime",
        [99] = "Documentary",
        [18] = "Drama",
        [10751] = "Family",
        [14] = "Fantasy",
        [36] = "History",
        [27] = "Horror",
        [10402] = "Music",
        [9648] = "Mystery",
        [10749] = "Romance",
        [878] = "Science Fiction",
        [10770] = "TV Movie",
        [53] = "Thriller",
        [10752] = "War",
        [37] = "Western",
        [10759] = "Action & Adventure",
        [10762] = "Kids",
        [10763] = "News",
        [10764] = "Reality",
        [10765] = "Sci-Fi & Fantasy",
        [10766] = "Soap",
        [10767] = "Talk",
        [10768] = "War & Politics"
    };

    public static string? GetEnglishName(int genreId) => _englishNames.GetValueOrDefault(genreId);
}
