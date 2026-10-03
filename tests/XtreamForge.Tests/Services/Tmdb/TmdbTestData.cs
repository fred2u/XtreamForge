namespace XtreamForge.Tests.Services.Tmdb;

internal static class TmdbTestData
{
    // English genres on the provider side, French genres on TMDB: matched through the TMDB genre ids
    public const string MatrixProviderInfo = """
        {
          "info": {
            "releasedate": "1999-03-31",
            "genre": "Action, Science Fiction",
            "cast": "Keanu Reeves, Laurence Fishburne",
            "movie_image": "https://image.tmdb.org/t/p/w600_and_h900_bestv2/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg"
          },
          "movie_data": { "name": "|FR| The Matrix (1999) 4K" }
        }
        """;

    public const string MatrixDetails = """
        {
          "id": 603,
          "title": "Matrix",
          "original_title": "The Matrix",
          "release_date": "1999-03-31",
          "poster_path": "/f89U3ADr1oiB1s9GkdPOEpXUk5H.jpg",
          "genres": [{ "id": 28, "name": "Action" }, { "id": 878, "name": "Science-Fiction" }],
          "credits": { "cast": [{ "name": "Keanu Reeves" }, { "name": "Laurence Fishburne" }] }
        }
        """;
}
