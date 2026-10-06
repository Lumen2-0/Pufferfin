namespace Pufferfin.Catalog.Tests.TestSupport;

/// <summary>
/// Trimmed payloads in the shape of the real APIs.
/// </summary>
public static class Fixtures
{
    public const string TmdbSearchMatrix = """
        {
          "page": 1,
          "total_pages": 1,
          "results": [
            { "id": 603, "media_type": "movie", "title": "Matrix", "original_title": "The Matrix", "release_date": "1999-03-30", "overview": "A hacker learns the truth.", "poster_path": "/matrix.jpg", "original_language": "en" },
            { "id": 604, "media_type": "movie", "title": "Matrix Reloaded", "original_title": "The Matrix Reloaded", "release_date": "2003-05-15", "overview": "Neo and his allies.", "poster_path": "/reloaded.jpg", "original_language": "en" },
            { "id": 6384, "media_type": "person", "name": "Keanu Reeves", "profile_path": "/keanu.jpg", "known_for_department": "Acting" },
            { "id": 1, "media_type": "collection", "name": "Not a title" }
          ]
        }
        """;

    /// <summary>
    /// A film with no French overview in its translations: the sheet must fall back to the original version.
    /// </summary>
    public const string TmdbMovieWithoutLocalizedOverview = """
        {
          "id": 550,
          "title": "Fight Club",
          "original_title": "Fight Club",
          "original_language": "en",
          "overview": "",
          "tagline": "",
          "release_date": "1999-10-15",
          "runtime": 139,
          "vote_average": 8.4,
          "poster_path": "/fc.jpg",
          "backdrop_path": "/fc-bd.jpg",
          "imdb_id": "tt0137523",
          "genres": [ { "id": 18, "name": "Drama" } ],
          "origin_country": [ "US" ],
          "production_countries": [ { "iso_3166_1": "US", "name": "United States of America" } ],
          "belongs_to_collection": null,
          "credits": {
            "cast": [ { "id": 819, "name": "Edward Norton", "character": "The Narrator", "profile_path": "/en.jpg" } ],
            "crew": [ { "id": 7467, "name": "David Fincher", "job": "Director" }, { "id": 1, "name": "Someone", "job": "Grip" } ]
          },
          "videos": {
            "results": [
              { "name": "Teaser EN", "site": "YouTube", "key": "teaser-en", "type": "Teaser", "iso_639_1": "en", "official": true },
              { "name": "Trailer FR", "site": "YouTube", "key": "trailer-fr", "type": "Trailer", "iso_639_1": "fr", "official": true },
              { "name": "Behind the scenes", "site": "YouTube", "key": "bts", "type": "Featurette", "iso_639_1": "fr", "official": true }
            ]
          },
          "recommendations": {
            "results": [ { "id": 680, "title": "Pulp Fiction", "release_date": "1994-09-10", "poster_path": "/pf.jpg" } ]
          },
          "translations": {
            "translations": [
              { "iso_639_1": "fr", "iso_3166_1": "FR", "data": { "title": "", "overview": "", "tagline": "" } },
              { "iso_639_1": "en", "iso_3166_1": "US", "data": { "title": "", "overview": "A ticking-time-bomb insomniac.", "tagline": "Mischief. Mayhem. Soap." } }
            ]
          },
          "external_ids": { "imdb_id": "tt0137523" }
        }
        """;

    /// <summary>
    /// <c>tv/1396</c> without appended blocks: specials, two regular seasons.
    /// </summary>
    public const string TmdbSeriesSeasons = """
        {
          "id": 1396,
          "name": "Breaking Bad",
          "seasons": [
            { "id": 3577, "season_number": 0, "name": "Specials", "episode_count": 9, "air_date": "2009-02-17", "poster_path": "/s0.jpg" },
            { "id": 3572, "season_number": 1, "name": "Season 1", "overview": "A teacher turns cook.", "episode_count": 3, "air_date": "2008-01-20", "poster_path": "/s1.jpg" },
            { "id": 3573, "season_number": 2, "name": "Season 2", "episode_count": 13, "air_date": "2009-03-08", "poster_path": null }
          ]
        }
        """;

    /// <summary>
    /// <c>tv/1396/season/1</c>: three episodes, the third without a title or an overview.
    /// </summary>
    public const string TmdbSeasonOne = """
        {
          "id": 3572,
          "season_number": 1,
          "name": "Season 1",
          "overview": "A teacher turns cook.",
          "air_date": "2008-01-20",
          "poster_path": "/s1.jpg",
          "episodes": [
            { "id": 62085, "season_number": 1, "episode_number": 2, "name": "Cat's in the Bag...", "overview": "Walt and Jesse clean up.", "air_date": "2008-01-27", "still_path": "/e2.jpg", "runtime": 48 },
            { "id": 62084, "season_number": 1, "episode_number": 1, "name": "Pilot", "overview": "Walter White learns he has cancer.", "air_date": "2008-01-20", "still_path": "/e1.jpg", "runtime": 59 },
            { "id": 62086, "season_number": 1, "episode_number": 3, "name": "", "overview": "", "air_date": "2008-02-10", "still_path": null, "runtime": null }
          ]
        }
        """;

    public const string AudibleSearchDune = """
        {
          "products": [
            {
              "asin": "B0FR4NC3D1",
              "title": "Dune",
              "authors": [ { "name": "Frank Herbert" } ],
              "narrators": [ { "name": "Benjamin Jungers" } ],
              "series": [ { "title": "Dune", "sequence": "1" } ],
              "language": "french",
              "release_date": "2021-09-01",
              "merchandising_summary": "<p>On Arrakis, <b>the desert</b> planet.</p>",
              "product_images": { "500": "https://m.media-amazon.com/images/dune-fr.jpg" }
            },
            {
              "asin": "B0EN6L1SH2",
              "title": "Dune",
              "authors": [ { "name": "Frank Herbert" } ],
              "language": "english",
              "release_date": "2007-12-31"
            }
          ]
        }
        """;

    public const string LetterboxdDiary = """
        <?xml version="1.0" encoding="utf-8"?>
        <rss version="2.0" xmlns:letterboxd="https://letterboxd.com" xmlns:tmdb="https://themoviedb.org">
          <channel>
            <title>Letterboxd - cinephile</title>
            <item>
              <title>Heat, 1995 - ★★★★★</title>
              <letterboxd:watchedDate>2026-09-30</letterboxd:watchedDate>
              <letterboxd:filmTitle>Heat</letterboxd:filmTitle>
              <letterboxd:memberRating>5.0</letterboxd:memberRating>
              <tmdb:movieId>949</tmdb:movieId>
            </item>
            <item>
              <title>Collateral, 2004 - ★★★★</title>
              <letterboxd:watchedDate>2026-09-20</letterboxd:watchedDate>
              <letterboxd:filmTitle>Collateral</letterboxd:filmTitle>
              <letterboxd:memberRating>4.0</letterboxd:memberRating>
              <tmdb:movieId>1538</tmdb:movieId>
            </item>
            <item>
              <title>Bad film, 2010 - ★</title>
              <letterboxd:filmTitle>Bad film</letterboxd:filmTitle>
              <letterboxd:memberRating>1.0</letterboxd:memberRating>
              <tmdb:movieId>9999</tmdb:movieId>
            </item>
            <item>
              <title>A list, not a film</title>
            </item>
          </channel>
        </rss>
        """;
}
