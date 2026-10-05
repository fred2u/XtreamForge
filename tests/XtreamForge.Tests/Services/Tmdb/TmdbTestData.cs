namespace XtreamForge.Tests.Services.Tmdb;

internal static class TmdbTestData
{
    // English genres on the provider side, French genres on TMDB: matched through the TMDB genre ids
    public const string LatticeProviderInfo = """
        {
          "info": {
            "releasedate": "1999-03-31",
            "genre": "Action, Science Fiction",
            "cast": "Aldo Ferrant, Desmond Varga",
            "movie_image": "https://image.tmdb.org/t/p/w600_and_h900_bestv2/a3Kq9ZtW7mLx2PbR8vNc5HdYe1J.jpg"
          },
          "movie_data": { "name": "|FR| The Lattice (1999) 4K" }
        }
        """;

    public const string LatticeDetails = """
        {
          "id": 603,
          "title": "Lattice",
          "original_title": "The Lattice",
          "release_date": "1999-03-31",
          "poster_path": "/a3Kq9ZtW7mLx2PbR8vNc5HdYe1J.jpg",
          "genres": [{ "id": 28, "name": "Action" }, { "id": 878, "name": "Science-Fiction" }],
          "credits": { "cast": [{ "name": "Aldo Ferrant" }, { "name": "Desmond Varga" }] }
        }
        """;
}
